# Copilot Instructions

## project guidelines
- publisher name for ContextRelay.VSExtension should be unified as 'KazushiKamegawa'.
- All code should be written in English and encoded in UTF-8 with BOM and Windows line endings (CRLF).
- All code should be well-documented with comments explaining the purpose of functions, classes, and important code blocks.
- UI must support internationalization, allowing both English and Japanese languages, with a language toggle button that applies changes instantly without requiring a restart. If a translation is missing, display the text in English as a fallback. If the language toggle fails, display an error message in the current language and log the issue for debugging.

## GitHub repository guidelines
- issue must be written in English and include a clear description of the problem, steps to reproduce, expected behavior, and actual behavior.
- pull request must be written in English and include a clear description of the changes made, the reason for the changes, and any relevant issue numbers.
- commit messages must be concise and descriptive, following the format: "type(scope): description", where type is one of feat, fix, docs, style, refactor, test, chore, and scope is the area of the code affected by the change.
- create issue before starting work, create branch, and link the PR to the issue.
- If the issue is too large, break it down into sub-issues and link them together.

## documentation guidelines
- documentation must be written in English and include clear explanations of the functionality, usage instructions, and examples.
- If the documentation includes code snippets, they should be properly formatted and tested to ensure they work as expected.
- Script must be written in PowerShell.
