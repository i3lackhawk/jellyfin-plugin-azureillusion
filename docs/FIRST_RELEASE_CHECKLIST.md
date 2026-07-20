# Checklista pierwszego wydania

## GitHub

- [ ] Utwórz publiczne repozytorium `jellyfin-plugin-azureillusion`.
- [ ] Umieść w nim zawartość tego folderu, z `README.md` w katalogu głównym.
- [ ] Sprawdź, czy plik `.github/workflows/release.yml` jest widoczny w repozytorium.
- [ ] Nie dodawaj klucza API AzureIllusion ani konfiguracji Jellyfin.
- [ ] Główna gałąź repozytorium nazywa się `main`.

Bez lokalnego programu Git wykonaj kroki z
[GITHUB_WEB_UPLOAD.md](GITHUB_WEB_UPLOAD.md). Workflow można uruchomić ręcznie
z zakładki `Actions`; sam utworzy tag i GitHub Release.

## Kontrola lokalna

Uruchom:

```powershell
.\prepare-release.ps1 -GitHubOwner i3lackhawk
```

Polecenie buduje wtyczkę, uruchamia testy, tworzy ZIP, generuje manifest i
sprawdza jego sumę kontrolną. Każdy błąd zatrzymuje proces.

## Publikacja

- [ ] Numer w `build.yaml` odpowiada wersji w pliku projektu.
- [ ] Utwórz tag `v0.1.0.0` dla pierwszego wydania.
- [ ] Wypchnij tag do GitHub.
- [ ] Poczekaj na zakończenie akcji `Wydanie wtyczki Jellyfin`.
- [ ] Sprawdź, czy powstał GitHub Release z ZIP-em oraz plikami sum kontrolnych.
- [ ] Sprawdź, czy powstała gałąź `catalog` z plikiem `manifest.json`.
- [ ] Otwórz publicznie adres manifestu w przeglądarce.

## Synology

- [ ] Dodaj adres manifestu w ustawieniach repozytoriów Jellyfin.
- [ ] Odśwież katalog i zainstaluj AzureIllusion.
- [ ] Uruchom ponownie pakiet Jellyfin.
- [ ] Ustaw `https://subs.azureillusion.ovh` i osobny klucz API.
- [ ] Sprawdź połączenie, listę grup i pobranie napisów dla jednej serii.
