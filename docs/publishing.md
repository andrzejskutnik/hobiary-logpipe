# Publikacja dokumentacji

Dokumentacja `MkDocs` może być publikowana automatycznie na `GitHub Pages`.

Jeśli repo nie jest hostowane na GitHub, użyj równoległej ścieżki opisanej w `Publikacja Self-Hosted`.

## Co jest już w repozytorium

Dodany workflow buduje dokumentację z pliku `mkdocs.yml`, a następnie publikuje katalog wynikowy jako artefakt `GitHub Pages`.

Plik workflow: `.github/workflows/docs.yml`.

Workflow używa oficjalnych akcji `configure-pages`, `upload-pages-artifact` i `deploy-pages`, więc odpowiada zalecanemu modelowi publikacji `GitHub Pages`.

## Wymagania po stronie GitHub

1. Wejdź w `Settings -> Pages`.
2. Ustaw `Source` na `GitHub Actions`.
3. Upewnij się, że workflow ma prawo do `Pages` i `id-token`.
4. Potwierdź, że repozytorium faktycznie jest hostowane na GitHub, bo tylko wtedy `GitHub Pages` zadziała end-to-end.

## Kiedy publikacja się uruchamia

- automatycznie po `push` na gałąź `main`,
- ręcznie przez `workflow_dispatch`.

## Lokalna weryfikacja przed publikacją

```powershell
.\.venv\Scripts\mkdocs.exe build --strict
```

To powinno być traktowane jako najtańszy check przed wypchnięciem zmian do dokumentacji.

Jeśli chcesz dodatkowo sprawdzić sam workflow przed pushem, zweryfikuj przynajmniej:

- czy `.github/workflows/docs.yml` istnieje na gałęzi `main`,
- czy `mkdocs.yml` wskazuje prawidłowy katalog `docs/`,
- czy `mkdocs build --strict` przechodzi lokalnie,
- czy w `Settings -> Pages` źródło publikacji to `GitHub Actions`.

## Co dokładnie publikuje workflow

- synchronizuje materiały z katalogu głównego repozytorium do `docs/_generated/`,
- buduje dokumentację do katalogu `site/`,
- publikuje wynik przez oficjalne akcje `upload-pages-artifact` i `deploy-pages`.

## Dalszy krok

Jeśli repozytorium będzie miało własną domenę lub niestandardową ścieżkę projektu, zaktualizuj `site_url` w `mkdocs.yml`.

Jeżeli repo nie jest na GitHub, ten workflow pozostaje poprawnym szablonem, ale sama publikacja `GitHub Pages` nie uruchomi się po stronie hostingu.