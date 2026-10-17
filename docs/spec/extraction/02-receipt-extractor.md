# Receipt Extractor

**Status:** Draft  
**Location:** `IReceiptExtractor`, `ReceiptFile`, `ExtractionResult` and `ExtractionException` in `Application/Abstractions/`. `VisionReceiptExtractor`, `ExtractionPrompt`, `ExtractionResponseParser` in `Infrastructure/Extraction/`. The prompt and reply format are explained in [docs/extraction-prompt.md](../../extraction-prompt.md).  
**Depends on:** 01-ai-gateway, 03-pdf-rendering.

## Scope

Turn a stored file into merchant, date, total, subtotal and tax. Owns the prompt and parsing. Decides no flags, statuses or retries.

## Behavior

- `ExtractAsync(ReceiptFile, ct)` returns `ExtractionResult`: merchant, date, total, subtotal, tax (all optional), `Model`, `PromptVersion`. `ReceiptFile` is the file's bytes and content type, so the caller reads the file from the image store.
- A JPG or PNG is sent as one image. A PDF is rendered and every page is sent in order in one request.
- The prompt has a version string that changes whenever the text changes. `Model` is the gateway's configured model (`IAiGateway.Model`).
- Reply format: JSON with fixed keys, `null` for what cannot be read, `yyyy-MM-dd` dates and plain numbers. The reply may be wrapped in a markdown fence or have text around it.
- Money is parsed as `decimal` with invariant culture and rounded to two decimals. A field the model leaves out, or a date that cannot be read, is missing, not an error. One unparseable field is a missing field.
- Throws `ExtractionException` when the gateway fails, the PDF cannot be rendered, the reply holds no JSON object, or merchant, date and total are all missing ("nothing usable"). The message is a short reason and never holds receipt content.
- Subtotal and tax exist only so the flag check can use them.
- A `FakeReceiptExtractor` for tests, in `tests/Application.Tests`, returns set results or throws on demand.

## Acceptance criteria

- A JPG is sent as one image. A 3-page PDF is sent as 3 images in page order, in one call.
- A full reply gives merchant, date and total. Subtotal and tax come through when present.
- A reply with no date gives an empty date and no exception.
- An unparseable reply, a reply with nothing usable, a gateway failure and a PDF that cannot be rendered each throw `ExtractionException`. No retry: the gateway is called once.
- A reply in a markdown fence, or with text around the JSON, is read.
- `19.99` and `0.00` parse to exact decimals.
- The result carries the configured model and the prompt version.
- A test pins the prompt text to its version.
- No log line contains receipt content.
