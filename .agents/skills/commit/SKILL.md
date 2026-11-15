---
name: commit
description: Commit the working tree with the pre-commit hook.
disable-model-invocation: true
---

# Commit the code

1. Stage everything, the user's own edits included, unless the user named what to commit.
2. Commit with a short message, split into several commits where the changes are unrelated.
3. Report the hook's errors if a commit fails.
