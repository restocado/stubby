# Stubby

Take a photo of a receipt, check the details Stubby picked up, and see where this month's money went.

Stubby is a single-user web app. You upload a receipt (JPG, PNG or PDF). A vision model reads the merchant, date and total in the background. You review the result, correct anything that is wrong, pick a category and approve it. Approved receipts feed monthly totals by category, with CSV export.

## Status

Design phase. There is no code yet, only documentation.

## How it works

1. Upload a receipt. It is queued and processed in the background, so you can keep uploading.
2. A vision model extracts the merchant, date and total. Suspicious results are flagged.
3. Open the receipt, confirm or correct the details and choose a category.
4. Approved receipts count toward your monthly totals. Approved receipts can never be edited.

## Planned stack

- .NET 10 and Blazor Web App (Interactive Server)
- PostgreSQL with EF Core
- A hosted background worker reading a database-backed queue
- Vision model behind an interface. First provider: Ollama Cloud with `gemma4:31b`

## Documentation

| Document | Contents |
|---|---|
| [Business requirements](docs/business-requirements.md) | Problem, goal, receipt statuses, scope, success criteria and the test set |
| [User stories](docs/user-stories.md) | User stories and the user flow |
| [Non-functional requirements](docs/non-functional-requirements.md) | Performance, accuracy, reliability, usability, data and security targets |
| [Architecture](docs/architecture.md) | Components, key decisions, main flows and rules |
| [Database design](docs/database-design.md) | Tables, columns, constraints, indexes and queue behavior |

## License

See [LICENSE](LICENSE).
