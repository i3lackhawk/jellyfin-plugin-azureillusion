# Repozytorium GitHub i publikowanie wydań

## 1. Utworzenie repozytorium

1. Utwórz na GitHub publiczne repozytorium `jellyfin-plugin-azureillusion`.
2. Jako zawartość repozytorium prześlij cały folder
   `jellyfin-plugin-azureillusion`, a nie cały projekt strony.
3. Główną gałęzią powinna być `main`.
4. Nie dodawaj klucza API ani plików konfiguracyjnych z serwera Jellyfin.

Repozytorium musi być publiczne, ponieważ Synology pobiera manifest i paczki bez
logowania do GitHub.

## 2. Pierwsze wydanie

Numer wersji jest zapisany w dwóch miejscach i skrypt kontroluje ich zgodność:

- `Jellyfin.Plugin.AzureIllusion/Jellyfin.Plugin.AzureIllusion.csproj`,
- `build.yaml`.

Dla wersji `0.1.0.0` utwórz i wypchnij tag `v0.1.0.0`. Workflow:

1. uruchomi testy,
2. zbuduje DLL,
3. utworzy `AzureIllusion_0.1.0.0.zip`,
4. obliczy MD5 i SHA-256,
5. utworzy GitHub Release,
6. opublikuje `manifest.json` na gałęzi `catalog`.

Nie twórz ręcznie gałęzi `catalog`. Workflow utworzy ją przy pierwszym wydaniu.

## 3. Adres dla Jellyfin

Po zakończeniu workflow adresem repozytorium jest:

```text
https://github.com/i3lackhawk/jellyfin-plugin-azureillusion/raw/refs/heads/catalog/manifest.json
```

Adres jest już skonfigurowany dla konta `i3lackhawk`. Nazwy paczek i adresy
wydań są generowane automatycznie na podstawie faktycznej nazwy repozytorium.

## 4. Kolejne wydanie

1. Zmień wersję w pliku projektu i `build.yaml`.
2. Uzupełnij `CHANGELOG.md` oraz pole `changelog` w `release-config.json`.
3. Uruchom lokalnie `build-plugin.ps1` i testy.
4. Zatwierdź zmiany.
5. Utwórz tag odpowiadający wersji, np. `v0.2.0.0`.
6. Wypchnij tag do GitHub.

Workflow zachowuje wcześniejsze wersje w manifeście, dzięki czemu Jellyfin może
poprawnie rozpoznać historię zgodności.

## 5. Zmiana nazwy konta lub repozytorium

Nie zmieniaj ręcznie adresów w kodzie wtyczki. Workflow pobiera właściciela i
nazwę z `GITHUB_REPOSITORY`. Po zmianie nazwy repozytorium opublikuj nowe
wydanie, a następnie podmień adres manifestu w ustawieniach Jellyfin.
