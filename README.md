# Polskie Napisy Anime dla Jellyfin

Wtyczka dostarcza polskie napisy z API AzureIllusion do Jellyfin. Jest
przygotowana do instalacji z własnego katalogu wtyczek, dlatego działa także z
Jellyfin zainstalowanym na Synology przez Centrum pakietów.

Plugin preferuje AniList ID zapisane w Jellyfin. Dla serialu sprawdza osobno
odcinek, jego sezon i dopiero dla sezonu pierwszego serię nadrzędną. Gdy
identyfikatora brakuje, może użyć jednoznacznego dopasowania dokładnego tytułu
sezonu i roku.

## Zgodność

- Jellyfin Server 12.1.0,
- .NET 10,
- napisy ASS i SRT,
- język polski (techniczne warianty PL i PL2) oraz inne języki udostępnione przez API,
- seriale, filmy, odcinki specjalne i odcinek 0.

Wersja `targetAbi` w katalogu musi odpowiadać wersji serwera Jellyfin. Przy
aktualizacji Jellyfin wtyczkę należy najpierw sprawdzić i wydać dla nowej wersji.

## Instalacja na Synology z repozytorium

Po opublikowaniu pierwszego wydania dodaj w Jellyfin adres:

```text
https://github.com/i3lackhawk/jellyfin-plugin-azureillusion/raw/refs/heads/catalog/manifest.json
```

Następnie otwórz `Panel administracyjny > Wtyczki > Katalog`, zainstaluj
Polskie Napisy Anime, uruchom ponownie Jellyfin i skonfiguruj wtyczkę. Pełna instrukcja
znajduje się w [docs/SYNOLOGY_INSTALL.md](docs/SYNOLOGY_INSTALL.md).

## Konfiguracja

### Kandydat 0.7.2.0

Wersja testowa dodaje zapisy częściowych zmian konfiguracji z wykrywaniem konfliktów
między kartami, historię i postęp zadań oraz podgląd dopasowania bez pobierania.
Ręczne zakresy mogą zawierać `targetSeason` i `episodeOffset`; numeracja absolutna
nie jest zgadywana. Pliki obejmujące kilka odcinków wymagają ręcznego wyboru napisów.

Konserwacja kolejkuje się zamiast pomijać zajęte zadanie. Pobieranie i aktualizacje
mają osobne budżety po 90 minut oraz trwałe punkty wznowienia. Wyszukiwanie powtarza
się po 24 godzinach (po błędzie po 15 minutach). Tryb wszystkich wersji uzupełnia
dozwolone wydania również wtedy, gdy odcinek ma już polskie napisy.

Rejestr sprawdzeń zapisywany jest partiami, z kopią poprzedniej wersji.
Nieczytelny rejestr zatrzymuje zadanie zamiast zerować historię. Podmiany plików
mają dziennik odzyskiwania; ręcznie zmienione napisy i kopie ratunkowe są chronione.
Pobieranie odrzuca puste pliki, pliki ponad 32 MB i niezgodny SHA-256 oraz ma limit
czasu także po odebraniu nagłówków odpowiedzi.

Przed uznaniem wydania za stabilne wymagane są testy na docelowym Jellyfin,
kontrola harmonogramu (pozostały stary wyzwalacz aktualizacji może nadal działać)
i obserwacja dwóch nocnych przebiegów. Nie usuwaj `downloads.json`, jego kopii
ani katalogu `transactions` w ramach aktualizacji.

Przy pierwszym uruchomieniu nowej wersji plugin sam wykonuje kopię konfiguracji,
rejestru, jego poprzedniej kopii i dzienników podmiany w sąsiednim katalogu
`AzureIllusion-backups/0.7.2.0`. Kopiuje wyłącznie lokalnie na serwerze, bez eksportu
sekretów. Weryfikuje SHA-256, zachowuje oryginał przy ponownych restartach, a błąd
kopii zatrzymuje uruchomienie dodatku przed zmianą danych. Panel administracyjny
może sprawdzić potwierdzenie przez endpoint `Plugins/PolskieNapisyAnime/upgrade-backup`.
Nie jest to kopia całego Jellyfin ani plików napisów obok wideo.

1. Otwórz `Panel administracyjny > Wtyczki > Polskie Napisy Anime`.
2. Ustaw adres API `https://subs.azureillusion.ovh`.
3. Wklej osobny klucz API utworzony wyłącznie dla Jellyfin.
4. Sprawdź połączenie i pobierz dynamiczną listę grup.
5. Wybierz biblioteki, języki, grupy oraz wszystkie wersje albo jedną najlepszą.
6. Zapisz konfigurację i włącz dostawcę napisów Polskie Napisy Anime.

## Dopasowanie anime

Kolejność dopasowania jest celowo zachowawcza:

1. ręczne mapowanie administratora pluginu,
2. identyfikator AniList odcinka lub sezonu zapisany przez dostawcę metadanych Jellyfin,
3. identyfikator serii nadrzędnej wyłącznie dla sezonu pierwszego,
4. opcjonalne, dokładne dopasowanie tytułu sezonu i tego samego roku.

Kolejne sezony nigdy nie dziedziczą identyfikatora pierwszego sezonu. Niejednoznaczny
wynik jest zawsze pomijany i trafia do diagnostyki pluginu. Lokalne mapowania
Kitsu/AniDB nie są obsługiwane.

## Wybór i pobieranie napisów

Domyślnie plugin korzysta ze wszystkich grup, również tych dodanych później.
Tryb konkretnych grup pokazuje zwijaną, przewijaną listę z wyszukiwarką,
licznikiem oraz przyciskami zaznaczania i czyszczenia.

Wtyczka pobiera grupy dynamicznie z API, więc dodanie grupy na stronie nie
wymaga nowego wydania wtyczki. Można ustawić minimalną ocenę, tylko
zweryfikowane wydania, limit grup i automatyczne pobieranie.

Przy dużych bibliotekach plugin automatycznie rozkłada zapytania w czasie,
respektuje `Retry-After` zwracane przez API i współdzieli dopasowanie serii
między jej odcinkami. Dzięki temu pełne skanowanie nie kończy się falą błędów
po przekroczeniu limitu zapytań.

Pobrane wydania są zapamiętywane na podstawie identyfikatora i sumy kontrolnej,
aby ten sam plik nie był ponownie pobierany dla tej samej pozycji biblioteki.

## Harmonogram zadań

Od wersji 0.7.1.0 limit 250 oznacza rozmiar jednej porcji zapytania, a nie limit
całego przebiegu. Aktualizacja sprawdza wszystkie kwalifikujące się wpisy.
Przełączniki pobierania i aktualizacji działają niezależnie. Niepowodzenie
pobierania nie blokuje etapu aktualizacji, ale przerwanie zadania zatrzymuje oba.

### Różne struktury sezonów

AniList zwykle ma osobny wpis dla każdej części anime, a Jellyfin grupuje je pod
jedną serią. Oficjalna wtyczka AniList v15 identyfikuje serie i filmy; nie należy
zakładać, że każdy Season w Jellyfin ma własne AniList ID. Dalsze sezony wymagają
własnych danych albo jednoznacznego tytułu/aliasu. ID rodzica służy tylko jako
fallback dla pierwszego sezonu o ogólnej nazwie.

Jeśli jeden sezon Jellyfin zawiera dwie części AniList, można jawnie określić
zakresy w polu ręcznych dopasowań (przykładowe identyfikatory):

```json
{
  "id:IDENTYFIKATOR_SERII_JELLYFIN#season:1": [
    {"anilistId":"12345","episodeStart":1,"episodeEnd":12,"episodeOffset":0},
    {"anilistId":"67890","episodeStart":13,"episodeEnd":24,"episodeOffset":-12}
  ]
}
```

Drugi zakres wyszukuje odcinek 1 drugiego anime dla odcinka 13 Jellyfin.
Nakładające się zakresy i odcinki bez pasującego zakresu blokują automatyczny
wybór. Nie przeliczamy numeracji na podstawie samego numeru sezonu.

Ręcznie pobrany plik trafia do rejestru aktualizacji po potwierdzeniu lokalnej
ścieżki i sumy SHA-256 podczas konserwacji. Niepotwierdzone wpisy oczekujące
wygasają po 24 godzinach; sam plik nie jest usuwany. Pliki pobierane z API muszą
być niepuste i nie większe niż 32 MB.

Po ponownym uruchomieniu Jellyfin w harmonogramie pojawi się jedno zadanie
`Polskie Napisy Anime — konserwacja napisów`. Codziennie o 04:00 najpierw
pobiera brakujące napisy, a następnie porcjami sprawdza aktualizacje plików
zarządzanych przez wtyczkę. Korzysta z bibliotek, języków, grup i trybu wersji
ustawionych bezpośrednio w pluginie.

Standardowe automatyczne zadanie napisów Jellyfin nie uruchamia tego dostawcy,
dzięki czemu nie powstają podwójne zapytania ani pobrania. Ręczne wyszukiwanie
napisów z poziomu odcinka lub filmu pozostaje dostępne.

## Miejsce zapisu napisów

Plugin nie zapisuje plików własnym, konkurencyjnym mechanizmem. Przekazuje je
standardowemu menedżerowi napisów Jellyfin:

- gdy biblioteka ma włączone zapisywanie napisów w folderze multimediów, Jellyfin próbuje zapisać plik obok wideo,
- gdy ta opcja jest wyłączona albo folder wideo nie pozwala na zapis, Jellyfin używa wewnętrznego katalogu metadanych.

Dzięki temu napisy z pluginu są zapisywane tak samo jak napisy od innych dostawców Jellyfin.

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
