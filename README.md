# AzureIllusion dla Jellyfin

Wtyczka dostarcza polskie napisy z API AzureIllusion do Jellyfin. Jest
przygotowana do instalacji z własnego katalogu wtyczek, dlatego działa także z
Jellyfin zainstalowanym na Synology przez Centrum pakietów.

Strona AzureIllusion zapisuje wyłącznie identyfikatory AniList. Wtyczka może
lokalnie przetłumaczyć identyfikator Kitsu albo AniDB znaleziony w Jellyfin na
AniList, ale nie dodaje Kitsu ani AniDB do bazy strony.

## Zgodność

- Jellyfin Server 10.11.11,
- .NET 9,
- napisy ASS i SRT,
- języki PL i PL2,
- seriale, filmy, odcinki specjalne i odcinek 0.

Wersja `targetAbi` w katalogu musi odpowiadać wersji serwera Jellyfin. Przy
aktualizacji Jellyfin wtyczkę należy najpierw sprawdzić i wydać dla nowej wersji.

## Instalacja na Synology z repozytorium

Po opublikowaniu pierwszego wydania dodaj w Jellyfin adres:

```text
https://github.com/i3lackhawk/jellyfin-plugin-azureillusion/raw/refs/heads/catalog/manifest.json
```

Następnie otwórz `Panel administracyjny > Wtyczki > Katalog`, zainstaluj
AzureIllusion, uruchom ponownie Jellyfin i skonfiguruj wtyczkę. Pełna instrukcja
znajduje się w [docs/SYNOLOGY_INSTALL.md](docs/SYNOLOGY_INSTALL.md).

## Konfiguracja

1. Otwórz `Panel administracyjny > Wtyczki > AzureIllusion`.
2. Ustaw adres API `https://subs.azureillusion.ovh`.
3. Wklej osobny klucz API utworzony wyłącznie dla Jellyfin.
4. Sprawdź połączenie i pobierz dynamiczną listę grup.
5. Wybierz strategię: najlepiej oceniane wydanie albo preferowane grupy.
6. W konfiguracji biblioteki włącz dostawcę napisów AzureIllusion.

## Dopasowanie anime

Kolejność dopasowania jest celowo zachowawcza:

1. identyfikator AniList zapisany w Jellyfin,
2. lokalne mapowanie `Kitsu -> AniList` lub `AniDB -> AniList`,
3. dokładne dopasowanie znormalizowanego tytułu i roku przez API AzureIllusion.

Niejednoznaczny wynik jest pomijany. Wtyczka nigdy nie przesyła identyfikatorów
Kitsu ani AniDB do strony AzureIllusion.

## Wybór i pobieranie napisów

Wtyczka pobiera grupy dynamicznie z API, więc dodanie grupy na stronie nie
wymaga nowego wydania wtyczki. Można ustawić minimalną ocenę, tylko
zweryfikowane wydania, limit grup i automatyczne pobieranie.

Pobrane wydania są zapamiętywane na podstawie identyfikatora i sumy kontrolnej,
aby ten sam plik nie był ponownie pobierany dla tej samej pozycji biblioteki.

## Bezpieczeństwo

- używaj wyłącznie adresu API HTTPS,
- utwórz osobny klucz API dla Jellyfin,
- nigdy nie zapisuj klucza w repozytorium GitHub,
- po ujawnieniu klucza natychmiast unieważnij go w panelu AzureIllusion.

Więcej informacji: [SECURITY.md](SECURITY.md).

## Budowanie i wydawanie

```powershell
.\build-plugin.ps1
.\update-manifest.ps1 -GitHubOwner i3lackhawk -GitHubRepository jellyfin-plugin-azureillusion
.\test-repository.ps1
```

Całą lokalną kontrolę można uruchomić jednym poleceniem:

```powershell
.\prepare-release.ps1 -GitHubOwner i3lackhawk
```

Skrypt budowania uruchamia testy, tworzy ZIP oraz sumy MD5 i SHA-256. MD5 jest
używane przez katalog Jellyfin, a SHA-256 służy do niezależnej kontroli paczki.

Publikowanie wydań opisuje [docs/GITHUB_RELEASE.md](docs/GITHUB_RELEASE.md).

## Dokumentacja

- [Instalacja na Synology](docs/SYNOLOGY_INSTALL.md)
- [Utworzenie repozytorium i wydanie](docs/GITHUB_RELEASE.md)
- [Pierwsza publikacja przez stronę GitHub, bez programu Git](docs/GITHUB_WEB_UPLOAD.md)
- [Checklista pierwszego wydania](docs/FIRST_RELEASE_CHECKLIST.md)
- [Utrzymanie wersji](docs/MAINTENANCE.md)

## Licencja

GPL-3.0-or-later. Szczegóły znajdują się w [LICENSE.md](LICENSE.md).
