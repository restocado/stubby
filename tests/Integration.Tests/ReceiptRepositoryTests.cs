using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

public class ReceiptRepositoryTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private Task<T> Repo<T>(Func<IReceiptRepository, Task<T>> action) =>
        InScopeAsync(sp => action(sp.GetRequiredService<IReceiptRepository>()));

    [Fact]
    public async Task Added_receipts_get_increasing_transaction_numbers_from_one()
    {
        var first = await AddQueuedAsync();
        var second = await AddQueuedAsync();
        var third = await AddQueuedAsync();

        Assert.Equal([1L, 2L, 3L], [first.TransactionNo, second.TransactionNo, third.TransactionNo]);
    }

    [Fact]
    public async Task A_second_receipt_with_the_same_checksum_is_refused_and_the_first_is_found()
    {
        var first = await AddQueuedAsync(checksum: "same-bytes");
        var duplicate = Receipt.Create("again.png", "image/png", 100, "same-bytes", "other-path.png", DateTimeOffset.UtcNow);

        var added = await TryAddAsync(duplicate);

        Assert.False(added);
        Assert.Equal(first.TransactionNo, await Repo(r => r.FindTransactionNoByChecksumAsync("same-bytes", Token)));
        Assert.Equal(1, await Fixture.WithDbAsync(db => db.Receipts.CountAsync(Token)));
    }

    [Fact]
    public async Task Lookup_of_an_unknown_checksum_finds_nothing()
    {
        await AddQueuedAsync();

        Assert.Null(await Repo(r => r.FindTransactionNoByChecksumAsync("never-uploaded", Token)));
    }

    [Fact]
    public async Task Eight_simultaneous_identical_inserts_give_one_winner()
    {
        var attempts = Enumerable.Range(0, 8)
            .Select(i => Task.Run(() => TryAddAsync(
                Receipt.Create($"copy-{i}.png", "image/png", 100, "race-bytes", $"path-{i}.png", DateTimeOffset.UtcNow)), Token));

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(added => added));
        Assert.Equal(1, await Fixture.WithDbAsync(db => db.Receipts.CountAsync(Token)));
    }

    [Fact]
    public async Task Sixty_receipts_page_as_25_25_and_10_newest_first()
    {
        for (var i = 0; i < 60; i++)
        {
            await AddQueuedAsync(minutesOld: 60 - i);
        }

        var first = await Repo(r => r.ListAsync(1, 25, Token));
        var second = await Repo(r => r.ListAsync(2, 25, Token));
        var third = await Repo(r => r.ListAsync(3, 25, Token));
        var past = await Repo(r => r.ListAsync(4, 25, Token));

        Assert.Equal([25, 25, 10, 0], [first.Items.Count, second.Items.Count, third.Items.Count, past.Items.Count]);
        Assert.All([first, second, third, past], page => Assert.Equal(60, page.TotalCount));
        var all = first.Items.Concat(second.Items).Concat(third.Items).ToList();
        Assert.Equal(all.OrderByDescending(r => r.CreatedAt).Select(r => r.Id), all.Select(r => r.Id));
        Assert.Equal(60, all.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public async Task A_huge_page_number_returns_an_empty_page_without_overflowing()
    {
        await AddQueuedAsync();

        var page = await Repo(r => r.ListAsync(int.MaxValue, 25, Token));

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Claim_takes_the_oldest_queued_receipt_and_sets_it_Processing()
    {
        await AddQueuedAsync(minutesOld: 1);
        var oldest = await AddQueuedAsync(minutesOld: 30);
        await AddQueuedAsync(minutesOld: 10);

        var claimed = await Repo(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token));

        Assert.NotNull(claimed);
        Assert.Equal(oldest.Id, claimed.Id);
        Assert.Equal(ReceiptStatus.Processing, claimed.Status);
        Assert.Equal(ReceiptStatus.Processing, (await Fixture.ReloadAsync(oldest.Id)).Status);
    }

    [Fact]
    public async Task Claim_with_nothing_queued_returns_null_and_ignores_other_statuses()
    {
        var processing = await AddQueuedAsync();
        Assert.NotNull(await Repo(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token)));

        var claimed = await Repo(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token));

        Assert.Null(claimed);
        Assert.Equal(ReceiptStatus.Processing, (await Fixture.ReloadAsync(processing.Id)).Status);
    }

    [Fact]
    public async Task Eight_simultaneous_claims_on_one_queued_receipt_give_it_to_exactly_one()
    {
        await AddQueuedAsync();

        var claims = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => Repo(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token)), Token)));

        Assert.Equal(1, claims.Count(c => c is not null));
    }

    [Fact]
    public async Task Simultaneous_claims_on_several_queued_receipts_each_get_a_different_one()
    {
        for (var i = 0; i < 5; i++)
        {
            await AddQueuedAsync(minutesOld: 10 - i);
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => Repo(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token)), Token)));

        Assert.All(claims, Assert.NotNull);
        Assert.Equal(5, claims.Select(c => c!.Id).Distinct().Count());
    }

    [Fact]
    public async Task Saving_a_claimed_receipt_stores_the_change_and_leaves_the_transaction_number_alone()
    {
        var added = await AddQueuedAsync();
        var claimed = await Repo(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token));
        claimed!.CompleteExtraction(
            "Saved Cafe", new DateOnly(2026, 10, 2), 12.50m, "stub-model:1b", "v1", isFlagged: true, "TotalAtOrAboveLimit", DateTimeOffset.UtcNow);

        await Repo(async r =>
        {
            await r.SaveAsync(claimed, Token);
            return true;
        });

        var reloaded = await Fixture.ReloadAsync(added.Id);
        Assert.Equal(ReceiptStatus.ForReview, reloaded.Status);
        Assert.Equal("Saved Cafe", reloaded.ExtractedMerchant);
        Assert.Equal(new DateOnly(2026, 10, 2), reloaded.ExtractedDate);
        Assert.Equal(12.50m, reloaded.ExtractedTotal);
        Assert.True(reloaded.IsFlagged);
        Assert.Equal("TotalAtOrAboveLimit", reloaded.FlagReasons);
        Assert.Equal(added.TransactionNo, reloaded.TransactionNo);
    }

    [Fact]
    public async Task A_total_with_fifteen_digits_and_two_decimals_round_trips()
    {
        var added = await AddQueuedAsync();
        var claimed = await Repo(r => r.ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token));
        claimed!.CompleteExtraction("Big", new DateOnly(2026, 10, 2), 9_999_999_999_999.99m, "m", "v1", true, null, DateTimeOffset.UtcNow);
        await Repo(async r =>
        {
            await r.SaveAsync(claimed, Token);
            return true;
        });

        Assert.Equal(9_999_999_999_999.99m, (await Fixture.ReloadAsync(added.Id)).ExtractedTotal);
    }
}
