# Pierwsza publikacja bez programu Git

Poniższa procedura pozwala utworzyć katalog wtyczki wyłącznie przez stronę
GitHub. Nie wymaga dostępu do katalogów pakietu Jellyfin na Synology.

## 1. Utworzenie publicznego repozytorium

1. Zaloguj się na `https://github.com`.
2. Wybierz `New repository`.
3. Ustaw nazwę `jellyfin-plugin-azureillusion`.
4. Wybierz `Public`.
5. Nie dodawaj automatycznie README, licencji ani `.gitignore`.
6. Kliknij `Create repository`.

Publiczność repozytorium jest potrzebna, ponieważ Jellyfin na Synology pobiera
manifest i paczkę bez logowania do GitHub.

## 2. Przesłanie przygotowanych źródeł

Do repozytorium prześlij zawartość folderu:

```text
jellyfin-plugin-azureillusion
```

Nie przesyłaj nadrzędnego projektu strony `strona` i nie dodawaj żadnego klucza
API. W katalogu głównym repozytorium muszą znaleźć się między innymi:

```text
.github/workflows/release.yml
Jellyfin.Plugin.AzureIllusion/
Jellyfin.Plugin.AzureIllusion.Tests/
README.md
build-plugin.ps1
build.yaml
```

Na stronie pustego repozytorium wybierz `uploading an existing file`, przeciągnij
zawartość folderu i zatwierdź ją do gałęzi `main`. Po przesłaniu koniecznie
otwórz zakładkę `Actions` i sprawdź, czy widoczna jest akcja
`Wydanie wtyczki Jellyfin`.

## 3. Pierwsze wydanie bez tworzenia tagu lokalnie

1. Otwórz `Actions`.
2. Wybierz `Wydanie wtyczki Jellyfin`.
3. Kliknij `Run workflow`.
4. Jako `Tag wydania` wpisz `v0.1.0.0`.
5. Uruchom workflow i poczekaj na zielony wynik wszystkich kroków.

Workflow sam utworzy wydanie, tag, paczkę ZIP, sumy kontrolne oraz gałąź
`catalog` z manifestem. Nie trzeba ręcznie edytować adresu paczki.

## 4. Adres do wklejenia w Jellyfin

Po udanym wydaniu użyj adresu:

```text
https://raw.githubusercontent.com/i3lackhawk/jellyfin-plugin-azureillusion/catalog/manifest.json
```

Adres jest już skonfigurowany dla konta `i3lackhawk`.
Adres API w ustawieniach samej wtyczki pozostaje niezależny:

```text
https://subs.azureillusion.ovh
```

## 5. Kontrola przed instalacją

Otwórz adres manifestu w prywatnym oknie przeglądarki. Prawidłowy wynik to JSON
z nazwą `AzureIllusion`, wersją `0.1.0.0`, sumą `checksum` i adresem
`sourceUrl`. Błąd `404` oznacza, że workflow nie utworzył jeszcze gałęzi
`catalog` albo adres zawiera niewłaściwy login.
