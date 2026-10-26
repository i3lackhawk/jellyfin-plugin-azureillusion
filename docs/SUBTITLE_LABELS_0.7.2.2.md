# Czytelne nazwy ścieżek napisów — 0.7.2.2

## Przyczyna

Wtyczka zwraca w `SubtitleResponse.Language` język, bezpieczną nazwę grupy i stabilną końcówkę `.pna-<12 znaków>`. Jellyfin używa tego pola do nazwy zapisywanego pliku. Resolver zewnętrznych napisów przenosi pozostałą część nazwy do tytułu ścieżki — stąd widoczne `mioro-subs.pna-…`.

Sprawdzono [zapis plików w Jellyfin](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Providers/Subtitles/SubtitleManager.cs), [odczyt metadanych](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Providers/MediaInfo/MediaInfoResolver.cs) i interfejsy zestawów 12.1.0 używanych do kompilacji. Samo `SubtitleResponse` nie ma oddzielnego pola tytułu.

## Rozwiązanie

`SubtitleLabelService` ustawia osobno `MediaStream.Title` na oryginalną nazwę grupy (np. `Mioro-Subs`), a `Language` na właściwy kod języka. Jellyfin składa z tych danych swoją etykietę z formatem i oznaczeniami. Sprawdzamy również rzeczywisty `MediaStream.DisplayTitle`, nie tylko pomocniczy tekst.

Nazwy plików, bajty, sumy kontrolne, ścieżki w rejestrze, identyfikatory wydań i historia pobrań pozostają niezmienione. Techniczny znacznik nadal istnieje na dysku i służy rozróżnianiu wydań. Nie ma masowego przemianowania ani migracji rejestru.

Po starcie serwis przechodzi wyłącznie przez pozycje obecne w rejestrze pobrań, z krótkimi przerwami między filmami. Zmiany zgłoszone przez Jellyfin są przetwarzane w ograniczonej kolejce po zdarzeniach `ItemAdded`/`ItemUpdated`. Podczas skanowania biblioteki zapis jest odkładany; po przepełnieniu kolejki ponawiany jest przegląd znanych pobrań.

Zmieniane są wyłącznie jednoznacznie powiązane, zewnętrzne ścieżki napisów. Rozpoznawane są potwierdzone ścieżki oraz pełne zarządzane nazwy oczekujących zapisów, w tym małe litery, katalog metadanych i numerowanie Jellyfin. Ręczne tytuły, inny język, ścieżki osadzone, audio/wideo i niejednoznaczne powiązania pozostają nietknięte. Dla znanego pobrania bez nazwy grupy używana jest etykieta „Polskie Napisy Anime”.

## Zakres testów i wdrożenia

Testy obejmują stare i nowe nazwy, polskie znaki, inne alfabety, emoji, ręczny tytuł, numerowany zapis w metadanych, flagi forced/SDH/default, obce ścieżki oraz niejednoznaczność. Kontrola serwisu korzysta z interfejsu repozytorium Jellyfin i sprawdza zapis tylko zmienionych metadanych, ponowienie po odświeżeniu i odłożenie podczas skanu.

Test deduplikacji wykonuje rzeczywisty kod dostawcy wtyczki z kontrolowanym HTTP i tymczasowymi plikami: pobranie → poprawa etykiety → restart rejestru → ponowny przebieg. Sprawdza jeden transfer i jeden zapis, niezmienione bajty oraz zachowanie identyfikatora w rejestrze.

Nie jest to test UI uruchomionego serwera Jellyfin. Na tym etapie nie opublikowano ani nie zainstalowano 0.7.2.2 i nie zmieniono katalogów wydań. Do kontroli po wdrożeniu: zaczekać na przegląd znanych pobrań, ponownie otworzyć odtwarzacz, sprawdzić stare i nowe ścieżki oraz powtórzyć odświeżenie metadanych i symulację braku ponownych pobrań.

Weryfikacja lokalna z 26.10.2026: pełny zestaw **141/141 zaliczonych, bez pominięć** (w tym 13 nowych przypadków). Test rejestracji sprawdza obecność usługi w kontenerze, a test zdarzenia biblioteki uruchamia usługę w tle, przetwarza `ItemUpdated` i potwierdza odpięcie zdarzeń przy zatrzymaniu. Build Release: **0 błędów, 0 ostrzeżeń**; kontrola różnic bez błędów. Istniejący ZIP 0.7.2.1 pozostał niezmieniony, SHA-256 `0b5492f7f027625fee3e2cd92d0e5c6b5513f6211ae89401c533df2b3b5cc337`.
