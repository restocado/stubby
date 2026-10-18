# Eval Module : Overview

**Status:** Built

Console app that generates the 30 synthetic test receipts and scores the extractor against known answers.

Separate console app. It references the Application abstractions and the Infrastructure extractor and runs receipts through `IReceiptExtractor` and the flag rules directly. No web app, database or queue. One folder per command (`Generate`, `Run`). `Generate` is built and its output is committed in `testset/`. `Run` is built, and is tested against a fake extractor that knows the answer key. The first run against the real model has not been made.

Usage, from the repository root: `dotnet run --project src/Eval -- generate [--out <folder>] [--seed <number>]`. The default folder is `testset` and the default seed is 20261017.

| File | Contents |
|---|---|
| [01-test-set-generator.md](01-test-set-generator.md) | Seeded generator and answer file |
| [02-run-eval.md](02-run-eval.md) | Run and score |
| [03-test-plan.md](03-test-plan.md) | Checks on the tool |
| [04-open-decisions.md](04-open-decisions.md) | Open items |

**Build order:** 01, then 02. 02 can start against the fake extractor.
