# PDF Rendering

**Status:** Draft  
**Location:** `IPdfRenderer` and `PdfRenderException` in `Application/Abstractions/`. `PdfToImageRenderer` in `Infrastructure/Pdf/`, using PDFtoImage 5.4.0 (MIT, with PDFium and SkiaSharp natives for Windows, Linux and macOS).  
**Depends on:** nothing.

## Scope

Render every page of a PDF to PNG, since the model accepts only images.

## Behavior

- Returns one PNG per page, in page order. No page limit, only the 10 MB file limit (NFR-S1).
- PDFium is not thread-safe, so every call is behind a process-wide lock.
- A corrupt PDF, text that is not a PDF, an empty input or a PDF with no pages throws `PdfRenderException` with a short reason. A caller's own cancellation is not wrapped.
- Rendering is in memory. Nothing is written to disk.
- `RenderAsync(ReadOnlyMemory<byte>, CancellationToken)` waits for the lock without blocking a thread, then renders on a pool thread. Pages are rendered and encoded one at a time, so a long PDF does not hold all its bitmaps in memory at once. The PNGs it returns are all in memory.
- DPI is the `Pdf:Dpi` setting, 150 to start with, allowed from 72 to 300 and checked at startup. Tune it on the eval set.
- There is no limit on page size, only on file size. A PDF with a very large page renders to a very large bitmap.
- PDFtoImage's platform annotations include more platforms than this app runs on, so the one call into it suppresses analyzer CA1416 with a comment.

## Acceptance criteria

- A 1-page PDF returns 1 PNG. A 5-page PDF returns 5, in order.
- A corrupt file and a zero-page PDF throw `PdfRenderException` without crashing the process.
- Concurrent calls on different PDFs each return the right pages.
- No file is left on disk.
- Every image starts with the PNG signature.
