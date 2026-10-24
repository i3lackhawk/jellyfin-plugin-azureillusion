# Ponowne pobieranie napisów — poprawka 0.7.2.1

Wtyczka sprawdza lokalny zapis przed wyszukiwaniem i ponownie tuż przed pobraniem. Nie musi czekać na etap aktualizacji, żeby rozpoznać ręcznie pobrany plik. Działa to również przy wyłączonych aktualizacjach.

## Co zostało zmienione

- Oczekujący wpis jest potwierdzany po znalezieniu jednego kompletnego pliku o zgodnej sumie SHA-256. Przerwanie po zapisie na dysku nie wymusza następnego pobrania.
- Zmiana ścieżki filmu może zachować powiązanie przez ID Jellyfin i sumę pliku. Jeśli zmieniły się również identyfikatory albo znaleziono kilka jednakowych kopii, automat nie zgaduje.
- Rozpoznawane są nazwy zapisane małymi literami, pliki w metadanych i numerowane końcówki `.0`, `.1` itd. Nie trzeba czekać na odświeżenie listy napisów Jellyfin.
- Indeks zawartości sprawdza wszystkie zapamiętane kopie. Brak pierwszej nie przesłania drugiej istniejącej.
- Sprawdzenie, pobranie i potwierdzenie we własnym zadaniu są objęte blokadą dla danego pliku wideo. Nieaktualny wynik wyszukiwania nie wywołuje drugiego transferu.
- Po usunięciu lokalnego pliku ponowienie może zapisać aktualną wersję źródła wraz z jej nową sumą. Nie zostaje przy niej stara suma z historii.

Przy niekompletnym, niepotwierdzonym zapisie ponowienie pozostaje możliwe: Jellyfin tworzy nową, numerowaną nazwę, a poprzedni plik pozostaje nietknięty. Potwierdzamy dopiero poprawną kopię. Niejednoznaczne powiązanie, błąd dostępu albo ręczna zmiana potwierdzonego pliku nie prowadzą do jego nadpisania.

Sposób zapisu nazw i plików sprawdzono także w [kodzie menedżera napisów Jellyfin](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Providers/Subtitles/SubtitleManager.cs). Testy odtwarzają małe litery, zapis z `CreateNew`, numerowanie i katalog metadanych.

Nie usuwamy historycznych duplikatów, nie zmieniamy przypisań anime i sezonów oraz nie zerujemy historii. Ręczny wybór napisów nadal pozwala świadomie pobrać wybrane wydanie. Inne wydania pozostają dozwolone; aktualizacje nowych wersji nadal korzystają z dziennika odzyskiwania i kopii poprzedniego pliku.

## Raporty

Historia pokazuje osobno nowe pobrania, aktualizacje, pliki już obecne i odzyskane zapisy. Szczegóły raportu zawierają identyfikator wydania i wynik:

- `already-downloaded` / historyczne `alreadyDownloaded` — plik już był zapisany;
- `confirmed-local` — potwierdzono wcześniejszy zapis;
- `rebound-local` — znaleziono plik po przeniesieniu wideo;
- `incomplete-local` — zapis nie był kompletny; ponowienie nie usuwa poprzedniego pliku;
- `local-conflict` — niejednoznaczne powiązanie lub konflikt;
- `local-unavailable` — nie udało się odczytać magazynu;
- `downloaded` / `updated` — zakończone pobranie lub aktualizacja.

Licznik pobrań zwiększa się po potwierdzeniu pliku, nie po samym wywołaniu API. Błąd odczytu nie jest traktowany jako dowód braku napisów. Raporty nie zawierają klucza API ani treści napisów.

## Zgodność i instalacja

Format `downloads.json` pozostaje zgodny ze schematem 2. Nowe indeksy istnieją tylko w pamięci. Przy pierwszym starcie 0.7.2.1 działa dotychczasowy mechanizm sprawdzonej kopii konfiguracji, rejestru i dzienników do katalogu `AzureIllusion-backups/0.7.2.1`.

To kandydat, nie potwierdzone wdrożenie produkcyjne. Przygotowanie ZIP-a nie oznacza publikacji w katalogu, instalacji ani aktywowania wersji. Nie instalować w czasie odtwarzania lub trwającego zadania. Przed zmianą zachować również własną kopię stanu.

Po uzgodnionej instalacji i restarcie:

1. Potwierdzić w Jellyfin wersję **0.7.2.1 Active** i poprawną kopię startową.
2. Na jednym testowym odcinku uruchomić konserwację i pobrać jej raport. Sprawdzić faktyczną ścieżkę i liczbę plików.
3. Od razu uruchomić symulację, która pomija 24-godzinny harmonogram wyszukiwania. Nie może planować ponownego pobrania tego samego wydania. Następnie porównać dwa rzeczywiste przebiegi obejmujące tę pozycję, np. po upływie 24 godzin: w drugim nie może być ponownego pobrania. Zwykły drugi przebieg przed upływem 24 godzin, który pominął pozycję, sam w sobie nie dowodzi naprawy.
4. Powtórzyć z ręcznym pobraniem oraz wyłączonymi aktualizacjami. Istniejący plik ma zostać rozpoznany bez kolejnego transferu.
5. Porównać identyfikatory i sumy, a nie tylko tytuły. Inna grupa albo rzeczywiście zmieniona wersja nie jest duplikatem.

Nie wykonywać automatycznego czyszczenia wcześniejszych kopii ani usuwać rejestru, żeby wymusić test. Bez logów i raportów docelowego serwera nie uznajemy, że potwierdzono konkretne wcześniejsze duplikaty w bibliotece użytkownika.

## Testy lokalne

`DownloadDeduplicationTests` używa rzeczywistego klienta API i dostawcy wtyczki z kontrolowanymi odpowiedziami HTTP oraz tymczasowego magazynu. Liczy żądania pobrania, zapisy i sumy plików. Kod pobierania współdzielony z zadaniem oraz rzeczywista ścieżka podmiany aktualizacji są wykonywane w testach; nie jest to uruchomiony pełny serwer Jellyfin.

Scenariusze obejmują dwa przebiegi i restart, aktualizacje wyłączone, przerwanie po zapisie, zmianę ścieżki, indeks identycznych kopii, brakujący i częściowy plik, niejednoznaczność, blokadę dysku, symulację bez zmian historii, równoległe wyniki, anulowanie oczekiwania, inne wydanie, nową wersję oraz ręczną zmianę w trakcie transferu.

Weryfikacja z 24.10.2026:

- Pełny zestaw na .NET 10 po dodatkowej kontroli serwera: **128/128 zaliczonych, bez pominięć**, w tym **23 scenariusze deduplikacji i 3 testy ponownego zapisu raportów**.
- Kompilacja Release: **0 błędów i 0 ostrzeżeń**. Składnia skryptu panelu i kontrola różnic również przeszły.
- Paczka `artifacts/AzureIllusion_0.7.2.1.zip`: sprawdzona zawartość, MD5 katalogu, SHA-256 archiwum, zgodność DLL z kompilacją i wersja zestawu `0.7.2.1`.
- SHA-256 nowej paczki, po poprawce raportów: `0b5492f7f027625fee3e2cd92d0e5c6b5513f6211ae89401c533df2b3b5cc337`.
- SHA-256 istniejącej paczki 0.7.2.0 pozostało bez zmian: `f521f273bf8fc4bebfba290373c6eaa8a3c24558488355c956c2995180dc0b7f`.
- Przy zakończeniu testów lokalnych przygotowano podgląd manifestu. Do publikacji zatwierdzono osobne wydanie `candidate-0.7.2.1` i aktualizację `candidate-catalog/manifest.json`; stabilny katalog ma pozostać bez zmian.

Na etapie zakończenia testów lokalnych nie wykonano jeszcze instalacji, restartu ani pobrań testowych na produkcji. Wyniki późniejszego wdrożenia zapisujemy osobno. Samo zaliczenie tych testów nie potwierdza dwóch przebiegów na docelowym Jellyfin.
