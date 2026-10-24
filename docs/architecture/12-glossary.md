# 12. Glossary

| Term | Meaning |
| :--- | :--- |
| Attachment / resource | A file uploaded into a note; stored once, referenced by `:/<id>`. "Resource" is the name used in the API. |
| Administrator | A user with the `admin` role: can create, change and delete accounts. Cannot read other users' notes. |
| CRA | EU Cyber Resilience Act, Regulation (EU) 2024/2847: cybersecurity requirements for products with digital elements. |
| Conflict (409) | A save based on an older state of the note than the stored one; the client asks which text to keep. |
| Draft | Unsaved text kept in the browser's `localStorage` until it has been saved. |
| Export | ZIP of all active notes of a user as Markdown files with front matter and attachments. |
| Front matter | YAML block at the start of an exported Markdown file (title, id, dates, tags, to-do state). |
| Notebook | A folder for notes; notebooks can be nested. |
| Purge | Permanent deletion from the trash. |
| SBOM | Software Bill of Materials: the list of all components in a release (CycloneDX format). |
| Session | A signed-in browser or API client, identified by a random token in a cookie or bearer header. |
| To-do | A note with a checkbox, optional due date and completion time. |
| Trash | Deleted notes and notebooks, restorable until purged. |
| Version | An earlier title and body of a note, kept by the version history. |
