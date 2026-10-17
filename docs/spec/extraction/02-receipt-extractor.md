# Receipt Extractor

**Status:** Draft  
**Location:** `IReceiptExtractor` and `ExtractionResult` in `Application/Abstractions/`. `VisionReceiptExtractor`, `ExtractionPrompt`, `ExtractionResponseParser` in `Infrastructure/Extraction/`.  
**Depends on:** 01-ai-gateway, 03-pdf-rendering.

## Scope

Turn a stored file into merchant, date, total, subtotal and tax. Owns the prompt and parsing. Decides no flags, statuses or retries.

## Behavior

- `ExtractAsync(ReceiptFile, ct)` returns `ExtractionResult`: merchant, date, total, subtotal, tax (all optional), `Model`, `PromptVersion`.
- A JPG or PNG is sent as one image. A PDF is rendered and every page is sent in order in one request.
- The prompt has a version string that changes whenever the text changes.
- Money is parsed as `decimal` with invariant culture. A field the model leaves out, or a date that cannot be read, is missing, not an error.
- Throws `ExtractionException` only when the gateway fails or the reply cannot be parsed as the expected structure at all.
- Subtotal and tax exist only so the flag check can use them.
- A `FakeReceiptExtractor` for tests returns set results or throws on demand.

## Acceptance criteria

- A JPG is sent as one image. A 3-page PDF is sent as 3 images in page order, in one call.
- A full reply gives merchant, date and total. Subtotal and tax come through when present.
- A reply with no date gives an empty date and no exception.
- An unparseable reply and a gateway failure each throw `ExtractionException`. No retry.
- `19.99` and `0.00` parse to exact decimals.
- The result carries the configured model and the prompt version.
- A test pins the prompt text to its version.
- No log line contains receipt content.
