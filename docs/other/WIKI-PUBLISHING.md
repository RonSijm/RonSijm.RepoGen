# Publish the docs as a wiki

The [publish-wiki workflow](../../.github/workflows/publish-wiki.yml) copies the repository docs to
the GitHub wiki. Edit the source Markdown here; generated wiki pages are replaced on the next run.

## Setup

1. Enable **Wikis** in the repository settings.
2. Create and save the first wiki page once.
   - GitHub creates the separate wiki Git repository when you do this. See
     [GitHub's wiki setup](https://docs.github.com/en/communities/documenting-your-project-with-wikis/adding-or-editing-wiki-pages#cloning-wikis-to-your-computer).
3. Let the workflow try the normal `GITHUB_TOKEN`.
   - If it cannot clone or push the wiki, add the repository Actions secret `WIKI_PUSH_TOKEN`
     with a token that can read and write this repository's wiki.
   - Keep the token in the secret. It does not belong in a script, URL or committed configuration.
4. Run **publish-wiki** from Actions, or push a documentation change to `main`.

The manual run only publishes from the repository's default branch. Wiki Git branches are
independent of the source repository's branches; the workflow pushes the wiki's existing default.

## What gets published?

- The main `README.md` becomes **Home**.
- `docs/README.md` becomes **Documentation**.
- Markdown files under `docs/` become individual pages.
  - Folder names are included in page filenames, so repeated `README.md` names do not collide.
  - A grouped sidebar keeps Implemented, Work in progress, Future work and Other separate.
- Links to exported docs become wiki links.
  - Links to examples and code point back to the matching source commit on GitHub.
  - Source line numbers become `#L...` links. Images use raw source URLs.
  - Code blocks are left alone.

The workflow does not regenerate the main README or build the .NET projects.

## Generated versus handwritten pages

- Home and the sidebar are managed by the publisher, including the initial wiki Home page.
- Other generated pages have an ownership marker and are listed in `.repogen-generated-files`.
  - Renamed/removed source docs remove only their previously generated wiki pages.
  - Handwritten pages outside that set are kept. A filename collision fails rather than replacing them.
- Manifests, linked files and output paths are validated before writes.
  - The scripts never clear the whole wiki checkout or recursively delete a supplied output directory.
- Repeated runs with unchanged input produce no new wiki commit.
  - Runs are serialized; pushes are ordinary Git pushes, never force pushes.

## Try the export locally

Run from the repository root with PowerShell 7:

```powershell
pwsh -NoProfile -File build/Scripts/Docs/test-wiki-tools.ps1
pwsh -NoProfile -File build/Scripts/Docs/export-docs-to-wiki.ps1 `
  -OutputPath ./build/Artifacts/Wiki `
  -Repository RonSijm/RonSijm.RepoGen `
  -RefName main
```

Output is ignored by Git. Local export does not clone or push anything.
