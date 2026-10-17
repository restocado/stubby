using System.Net;
using System.Text;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Features.Receipts.ApproveReceipt;
using Stubby.Domain.Receipts;

namespace Stubby.Integration.Tests;

/// <summary>The real app over HTTP, on the test database, with the stubbed model and no background worker.</summary>
public class ReviewHttpTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% a tiny stand-in, never rendered here\n");

    private HttpClient Client => Fixture.CreateWebClient();

    private async Task<string> GetPageAsync(string url)
    {
        var response = await Client.GetAsync(url, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Token);
    }

    private async Task<Guid> ReadAsync(byte[]? content = null, string fileName = "receipt.png", string contentType = "image/png")
    {
        var id = content is null ? await UploadAsync() : await UploadAsync(content, fileName, contentType);
        Assert.IsType<ProcessNextReceiptResult.Completed>(await ProcessOneAsync());
        return id;
    }

    [Fact]
    public async Task The_image_endpoint_serves_the_uploaded_bytes_with_their_type_and_safe_headers()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7, 6, 5];
        var id = await ReadAsync(png);

        var response = await Client.GetAsync($"/api/receipts/{id}/image", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("private", Assert.Single(response.Headers.GetValues("Cache-Control")), StringComparison.Ordinal);
        Assert.NotEqual("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task The_image_endpoint_serves_a_pdf_as_a_pdf()
    {
        var id = await ReadAsync(PdfBytes, "receipt.pdf", "application/pdf");

        var response = await Client.GetAsync($"/api/receipts/{id}/image", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PdfBytes, await response.Content.ReadAsByteArrayAsync(Token));
    }

    [Fact]
    public async Task The_image_of_a_Queued_receipt_is_a_conflict()
    {
        var id = await UploadAsync();

        var response = await Client.GetAsync($"/api/receipts/{id}/image", Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_image_of_a_Processing_receipt_is_a_conflict()
    {
        var id = await UploadAsync();
        await InScopeAsync(sp => sp.GetRequiredService<IReceiptRepository>().ClaimNextQueuedAsync(DateTimeOffset.UtcNow, Token));

        var response = await Client.GetAsync($"/api/receipts/{id}/image", Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("not-a-guid")]
    [InlineData("..%2f..%2fappsettings.json")]
    public async Task An_unknown_id_or_anything_but_an_id_is_not_found(string id)
    {
        var response = await Client.GetAsync($"/api/receipts/{id}/image", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_image_of_a_receipt_whose_file_is_gone_is_not_found()
    {
        var id = await ReadAsync();
        File.Delete(Path.Combine(Fixture.ImageRoot, (await Fixture.ReloadAsync(id)).ImagePath));

        var response = await Client.GetAsync($"/api/receipts/{id}/image", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_stored_path_outside_the_storage_root_is_never_served()
    {
        var id = await ReadAsync();
        await Fixture.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"UPDATE \"Receipts\" SET \"ImagePath\" = '../../appsettings.json' WHERE \"Id\" = {id}", Token);
            return 0;
        });

        var response = await Client.GetAsync($"/api/receipts/{id}/image", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_review_page_shows_the_number_the_extracted_fields_and_the_image()
    {
        var id = await ReadAsync();

        var html = await GetPageAsync($"/receipts/{id}");

        Assert.Contains("TXN-000001", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Fake Cafe\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"2026-10-02\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"12.50\"", html, StringComparison.Ordinal);
        Assert.Contains($"api/receipts/{id}/image", html, StringComparison.Ordinal);
        Assert.Contains("Choose a category", html, StringComparison.Ordinal);
        Assert.Contains("Nothing is saved until you approve.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Please check this receipt", html, StringComparison.Ordinal);
        foreach (var category in Enum.GetValues<ReceiptCategory>())
        {
            Assert.Contains(category.ToString().ToLowerInvariant(), html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Every_receipt_that_can_be_opened_has_a_back_link_to_the_list_at_the_top()
    {
        var forReview = await ReadAsync();
        var approved = await ReadAsync();
        await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new ApproveReceiptCommand(approved, "Final Cafe", new DateOnly(2026, 10, 3), 13.00m, ReceiptCategory.Dining), Token));
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        var failed = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Failed>(await ProcessOneAsync());

        foreach (var id in new[] { forReview, failed, approved })
        {
            var main = (await GetPageAsync($"/receipts/{id}")).Split("<main")[^1];

            var backLink = main.IndexOf("Back to the receipts", StringComparison.Ordinal);
            Assert.True(backLink >= 0, $"Receipt {id} has no back link.");
            Assert.True(backLink < main.IndexOf("<h1", StringComparison.Ordinal), "The back link should come before the heading.");
            Assert.Contains("href=\"receipts\"", main[..backLink], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_flagged_receipt_shows_a_banner_with_the_reasons()
    {
        Fixture.Extractor.Result = StubExtractor.Sample with { Total = 5000.00m, Date = null, Subtotal = null, Tax = null };
        var id = await ReadAsync();

        var html = await GetPageAsync($"/receipts/{id}");

        Assert.Contains("Please check this receipt", html, StringComparison.Ordinal);
        Assert.Contains("The total is unusually large.", html, StringComparison.Ordinal);
        Assert.Contains("The date could not be read.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pdf_is_embedded_with_a_link_to_open_it_in_a_new_tab()
    {
        var id = await ReadAsync(PdfBytes, "receipt.pdf", "application/pdf");

        var html = await GetPageAsync($"/receipts/{id}");

        Assert.Contains("<iframe", html, StringComparison.Ordinal);
        Assert.Contains("Open the PDF in a new tab", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html.Split("<main")[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Failed_receipt_shows_why_and_an_empty_form_for_manual_entry()
    {
        Fixture.Extractor.Failure = new ExtractionException("The provider returned HTTP 503.");
        var id = await UploadAsync();
        Assert.IsType<ProcessNextReceiptResult.Failed>(await ProcessOneAsync());

        var html = await GetPageAsync($"/receipts/{id}");

        Assert.Contains("Stubby could not read this receipt", html, StringComparison.Ordinal);
        Assert.Contains("The provider returned HTTP 503.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"Fake Cafe\"", html, StringComparison.Ordinal);
        Assert.Contains("Approve", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Approved_receipt_is_read_only_with_no_approve_button()
    {
        var id = await ReadAsync();
        await InScopeAsync(async sp => await sp.GetRequiredService<IMediator>().Send(
            new ApproveReceiptCommand(id, "Final Cafe", new DateOnly(2026, 10, 3), 13.00m, ReceiptCategory.Dining), Token));

        var html = await GetPageAsync($"/receipts/{id}");

        Assert.Contains("approved and can no longer be changed", html, StringComparison.Ordinal);
        Assert.Contains("Final Cafe", html, StringComparison.Ordinal);
        Assert.Contains("13.00", html, StringComparison.Ordinal);
        Assert.Contains("dining", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Approve</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<input", html.Split("<main")[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_receipt_still_being_read_cannot_be_opened()
    {
        var id = await UploadAsync();

        var html = await GetPageAsync($"/receipts/{id}");

        Assert.Contains("still being read", html, StringComparison.Ordinal);
        Assert.DoesNotContain("TXN-000001", html.Split("<main")[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_receipt_says_so()
    {
        var html = await GetPageAsync($"/receipts/{Guid.NewGuid()}");

        Assert.Contains("Receipt not found", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_list_links_only_the_receipts_that_can_be_opened()
    {
        var readable = await ReadAsync();
        var queued = await UploadAsync();

        var html = await GetPageAsync("/receipts");

        Assert.Contains($"href=\"receipts/{readable}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"href=\"receipts/{queued}\"", html, StringComparison.Ordinal);
        Assert.Contains("TXN-000002", html, StringComparison.Ordinal);
    }
}
