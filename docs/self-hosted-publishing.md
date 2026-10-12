# Publikacja Self-Hosted

To repo nie jest hostowane na GitHub, więc `GitHub Pages` pozostaje tylko szablonem referencyjnym. Dla obecnego hostingu najpraktyczniejszy wariant to publikacja statycznego artefaktu `site/`.

## Rekomendowany przepływ

1. Zbuduj dokumentację lokalnie albo w CI.
2. Spakuj katalog `site/` do archiwum.
3. Wgraj paczkę na serwer statyczny lub do docroot reverse proxy.
4. Rozpakuj wynik do katalogu publikacji.

## Pakowanie lokalne

Repo zawiera skrypt:

```powershell
.\tools\package-docs.ps1
```

oraz wariant POSIX dla Linux/macOS:

```bash
./tools/package-docs.sh
```

Skrypt:

- uruchamia `mkdocs build --strict`,
- czyści katalog `artifacts/`,
- tworzy paczkę `artifacts/hobiary-logpipe-docs.zip`.

## Gdzie opublikować paczkę

Artefakt można rozpakować na:

- serwerze `nginx`,
- serwerze `Caddy`,
- katalogu statycznym wystawianym przez obecny hosting,
- dowolnym webroot obsługującym zwykłe pliki HTML/CSS/JS.

## Minimalna walidacja po publikacji

- strona główna otwiera się bez błędów,
- działa nawigacja między sekcjami,
- wyszukiwarka `MkDocs` indeksuje treść,
- odświeżenie strony głównej nie kończy się `404`.

## Wariant CI dla obecnego hostingu

Jeśli obecny serwer CI potrafi przechowywać artefakty jobów, uruchamiaj w pipeline tylko dwa kroki:

1. `mkdocs build --strict`
2. spakowanie `site/` do `hobiary-logpipe-docs.zip`

To wystarcza, by oddzielić build dokumentacji od właściwego wdrożenia na serwer statyczny.