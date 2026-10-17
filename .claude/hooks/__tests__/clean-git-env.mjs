// Imported first by every self-test file. A git hook (pre-push) runs the self-test with GIT_DIR and friends
// exported; inherited by the fixtures' child processes, they point every fixture `git init/config/commit` at
// the real repository. Removing them here keeps each fixture inside its own temp repo.
for (const key of Object.keys(process.env)) if (key.startsWith('GIT_')) delete process.env[key];
