# Project Instructions

## Chat History Logging (mandatory, every session)

### 1. Session start
At the start of every session, before responding to the first prompt, read `.chat-history/log.md` (if it exists) to load context from previous sessions.

### 2. After every response
After completing each response, append a new entry to `.chat-history/log.md` using exactly this format:

```
---
- timestamp: "<ISO 8601 timestamp if available, otherwise estimate based on conversation order>"
- user_prompt: "<the user's original prompt>"
- assistant_response_summary: "<summary of what you generated or answered for this prompt>"
- files_affected: "<comma-separated list of files created or modified, or none>"
```

### 3. File handling
- If `.chat-history/` or `log.md` does not exist, create it.
- Append only. Never delete, rewrite, or reorder previous entries.

### 4. Silent operation
Do all of this silently. Never ask for confirmation before reading or writing the log, and don't mention the logging in responses unless asked.

### Rules
- **files_affected**: Be precise. Only list files explicitly created or modified during that response. Do not list `.chat-history/log.md` itself. Use `none` if nothing changed.
- **Never skip an exchange**: every prompt/response pair must be logged, including short questions and follow-ups.
- **assistant_response_summary**: Keep it concise but specific. Mention function names, endpoints, classes, or key decisions made.
- Escape double quotes inside values (`\"`) so each entry stays well-formed.
