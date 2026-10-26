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

Powyższe testy lokalne nie są testem UI uruchomionego serwera Jellyfin. Wyniki późniejszego wdrożenia opisano poniżej. Po wdrożeniu należy ponownie otworzyć odtwarzacz; stary ekran może jeszcze pokazywać poprzednią etykietę.

Weryfikacja lokalna z 26.10.2026: pełny zestaw **141/141 zaliczonych, bez pominięć** (w tym 13 nowych przypadków). Test rejestracji sprawdza obecność usługi w kontenerze, a test zdarzenia biblioteki uruchamia usługę w tle, przetwarza `ItemUpdated` i potwierdza odpięcie zdarzeń przy zatrzymaniu. Build Release: **0 błędów, 0 ostrzeżeń**; kontrola różnic bez błędów. Istniejący ZIP 0.7.2.1 pozostał niezmieniony, SHA-256 `0b5492f7f027625fee3e2cd92d0e5c6b5513f6211ae89401c533df2b3b5cc337`.

## Instalacja i test na serwerze — 26.10.2026

Po zgodzie właściciela opublikowano [kandydata 0.7.2.2](https://github.com/i3lackhawk/jellyfin-plugin-azureillusion/releases/tag/candidate-0.7.2.2), z kodu `9b7fc2c7da950609b451eb7dc70b110e74dab935`. Zaktualizowano wyłącznie katalog kandydatów. Stabilny katalog pozostał na `c77308dbe8d54d2495581b16fea053a69574828a`.

- [Linux CI](https://github.com/i3lackhawk/jellyfin-plugin-azureillusion/actions/runs/40326155001): **141/141**, bez pominięć, błędów i ostrzeżeń kompilacji.
- ZIP pobrany z opublikowanego wydania zweryfikowano przed instalacją: SHA-256 `6d57018fad8b2b07f443c98bb0a36afe85d536406f56ea163392ffebf8ae9703`, MD5 `df3b8af10b2010253c2565626b188ba1`.
- Jellyfin 12.1.0: instalacja i restart HTTP 204. Bezpośrednio przed restartem nie było odtwarzania ani aktywnych zadań.
- Po restarcie **0.7.2.2 Active**, kopia startowa zweryfikowana, połączenie ze stroną prawidłowe.

Kontrola przez API działającego Jellyfin, nie tylko przez pomocnicze funkcje:

1. Stara ścieżka w Hunter × Hunter zmieniła etykietę z `inne.pna-5161758da577 - Polish - ASS - Zewnętrzny` na `INNE - Polish - ASS - Zewnętrzny`. Jej ścieżka i 24 414 bajtów pozostały bez zmian; SHA-256 przed i po: `0bc7499004f06e8b29896eab46d627337b04ffa7cb833f1e0addf60d88cff241`.
2. W Genjitsu Shugi Yuusha no Oukoku Saikenki potwierdzono etykiety **FrixySubs**, **INNE**, **Shisha** i **Twoja Mała Japonia**. Po kontrolowanym odświeżeniu metadanych jednego odcinka (bez wymiany wszystkich danych i bez odświeżania obrazów) etykiety pozostały czytelne. Ścieżki i sumy wszystkich czterech napisów nie zmieniły się.
3. Symulacja dla jednego istniejącego odcinka Hunter × Hunter zakończyła się `completed`: sprawdzono 1 odcinek, licznik już pobranych 2, planowane pobrania **0**, błędy **0**, zapisy rejestru **0**. Czas 188,4 s, przebieg `1c1ada439d694307bdb17d7516f09be2`. Nie pobierano nowych napisów.
4. Po symulacji przywrócono obie wybrane biblioteki i potwierdzono identyczną rewizję całej konfiguracji. Zadanie jest bezczynne. W logach od startu 0.7.2.2 nie stwierdzono błędów wtyczki ani ostrzeżeń usługi etykiet.

Przegląd całej historii działa stopniowo w tle; kontrola próbek nie dowodzi zakończenia poprawy wszystkich wpisów. Nie przeprowadzono pełnego nocnego pobierania ani testu wizualnego we wszystkich klientach Jellyfin. Widoczną etykietę potwierdzono w rzeczywistym `DisplayTitle` zwracanym przez serwer.
