# PDF Rendering

**Status:** Draft  
**Location:** `IPdfRenderer` in `Application/Abstractions/`. `PdfToImageRenderer` in `Infrastructure/Pdf/`, using PDFtoImage.  
**Depends on:** nothing.

## Scope

Render every page of a PDF to PNG, since the model accepts only images.

## Behavior

- Returns one PNG per page, in page order. No page limit, only the 10 MB file limit (NFR-S1).
- PDFium is not thread-safe, so every call is behind a process-wide lock.
- A corrupt PDF or one with no pages throws `PdfRenderException`.
- Rendering is in memory. Nothing is written to disk.
- DPI is a setting.

## Acceptance criteria

- A 1-page PDF returns 1 PNG. A 5-page PDF returns 5, in order.
- A corrupt file and a zero-page PDF throw `PdfRenderException` without crashing the process.
- Concurrent calls on different PDFs each return the right pages.
- No file is left on disk.
- Every image starts with the PNG signature.
