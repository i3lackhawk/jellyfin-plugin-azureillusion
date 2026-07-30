# Historia zmian

## 0.4.1.0 - 2026-07-30

- opcjonalne szczegółowe logi diagnostyczne w standardowym dzienniku Jellyfin,
- identyfikatory operacji dla wyszukiwania i pobierania napisów,
- czas odpowiedzi, kod HTTP i numer próby dla wywołań API bez parametrów zapytania i klucza API,
- tryb diagnostyczny domyślnie wyłączony i dostępny w ustawieniach pluginu.

## 0.4.0.0 - 2026-07-30

- nazwa grupy napisów widoczna w Jellyfin jako część języka zapisanych napisów,
- nowe zadanie harmonogramu aktualizujące wcześniej pobrane napisy po zmianie pliku na stronie,
- zachowanie lokalnych napisów, gdy wydanie zostanie usunięte lub przestanie być dostępne na stronie,
- trwały i zgodny wstecznie rejestr pobrań zarządzanych przez plugin,
- blokada równoczesnego wykonywania zadań pobierania i aktualizacji napisów.

## 0.3.1.0 - 2026-07-29

- aktualizacja Microsoft.Extensions.Http do najnowszej poprawki .NET 9,
- aktualizacja narzędzi testowych Microsoft.NET.Test.Sdk i xUnit runner,
- zachowana zgodność ABI z Jellyfin Server 10.11.11 i platformą net9.0,
- pełny build Release oraz 15 testów pluginu bez błędów.

## 0.3.0.0 - 2026-07-28

- dedykowane zadanie Jellyfin „Polskie Napisy Anime — pobierz brakujące napisy”,
- pobieranie oparte na językach i bibliotekach wybranych bezpośrednio w pluginie,
- zapis przez standardowy menedżer Jellyfin, zgodny z miejscem zapisu ustawionym dla biblioteki i z fallbackiem do katalogu metadanych,
- domyślny wybór wszystkich grup, także grup dodanych później,
- zwijany i przewijany selektor grup z wyszukiwarką, licznikiem oraz szybkimi akcjami,
- bezpieczny fallback numeru sezonu tylko dla jednoznacznego wyniku,
- czytelne logi braku dopasowania, braku wyników API i błędów zapisu,
- obsługa wszystkich wersji albo jednej najlepszej także w zadaniu pluginu,
- szybsza pamięć pobranych wydań bez wielokrotnego odczytu pliku JSON,
- stronicowane pobieranie pełnej listy grup oraz przygotowanie obsługi aliasów anime.

## 0.2.0.0 - 2026-07-22

- nowa nazwa „Polskie Napisy Anime” i oryginalne logo,
- wybór wielu bibliotek i języków,
- tryb wszystkich wersji albo jednej najlepszej,
- test połączenia, grup i języków wykonywany przez serwer Jellyfin,
- usunięcie lokalnego mapowania identyfikatorów,
- dokładniejszy opis awaryjnego dopasowania tytułu i roku.

## 0.1.0.1 - 2026-07-21

- zgodność z Jellyfin Server 10.11.11,
- usunięcie operacji zapisu z etapu uruchamiania wtyczki,
- zabezpieczenie serwera Synology przed zatrzymaniem podczas inicjalizacji wtyczki.

## 0.1.0.0 - 2026-07-19

- pierwsza wersja repozytorium Jellyfin dla AzureIllusion,
- pobieranie napisów ASS i SRT przez HTTPS,
- dynamiczna lista grup z API,
- wybór najlepiej ocenianych lub preferowanych grup,
- lokalne mapowanie Kitsu i AniDB na AniList,
- ochrona przed ponownym pobraniem tego samego wydania,
- automatyczne wydania GitHub i katalog dla instalacji na Synology.
