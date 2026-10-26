# Historia zmian

Opisy odnoszą się do zmian w danym wydaniu. Zachowanie starszych wersji może różnić się od obecnego.

## 0.7.2.2 - kandydat, 2026-10-26

Lista napisów w Jellyfin pokazuje czytelną nazwę grupy zamiast technicznej końcówki `.pna-…`. Wtyczka poprawia metadane swoich ścieżek po uruchomieniu i po odświeżeniu filmu lub odcinka. Nie zmienia nazw ani zawartości plików, nie zeruje historii i nie pobiera napisów ponownie. Ręcznie nadane tytuły oraz napisy spoza rejestru wtyczki pozostają bez zmian.

Wydanie testowe dla Jellyfin 12.1. Wyniki instalacji i kontroli na serwerze: [czytelne nazwy napisów](docs/SUBTITLE_LABELS_0.7.2.2.md).

## 0.7.2.1 - kandydat, 2026-10-24

Poprawiamy sytuacje, w których wtyczka mogła ponownie pobrać napisy już zapisane na dysku.

- Istniejące pliki są sprawdzane przed pobieraniem, także gdy wyłączysz aktualizacje. Ręczne pobranie lub przerwane zadanie nie wymaga już czekania na etap aktualizacji, żeby zostało rozpoznane.
- Po zmianie ścieżki filmu wtyczka może zachować powiązanie po identyfikatorze Jellyfin i sumie pliku. Nie zgaduje po samym tytule.
- Rozpoznawane są pliki obok wideo i w metadanych, również z małymi literami w nazwie oraz końcówkami `.0`, `.1` dodanymi przez Jellyfin.
- Historia sprawdza wszystkie znane kopie o tej samej zawartości. Brak pierwszej nie powoduje już pomijania drugiej, istniejącej kopii.
- Ponowne sprawdzenie tuż przed zapisem chroni przed nieaktualnym wynikiem wyszukiwania. Równoległe wywołania własnego mechanizmu pobierania nie zapisują tego samego wydania dwa razy.
- Nie usuwamy wcześniejszych duplikatów ani nie nadpisujemy ręcznych zmian. Niepełny zapis można ponowić pod nową nazwą, zachowując poprzedni plik. Problem z dostępem do dysku zatrzymuje pobieranie; ręczny wybór nadal jest dostępny.
- Poprawiliśmy również błąd potwierdzony w nocnych raportach: po wyczerpaniu czasu zadania ponowny zapis tego samego raportu nie kończy już konserwacji błędem „plik już istnieje”.

To nadal wersja testowa. Instrukcja sprawdzenia na serwerze: [naprawa ponownych pobrań](docs/DOWNLOAD_DEDUP_0.7.2.1.md).

## 0.7.2.0 - kandydat, 2026-10-18

To wydanie testowe. Dodaliśmy podgląd dopasowania bez pobierania oraz postęp i historię zadań. Ustawienia otwarte w dwóch kartach są chronione przed przypadkowym nadpisaniem.

- Przed pierwszą zmianą danych wtyczka robi lokalną kopię ustawień i historii pobrań oraz sprawdza jej sumy SHA-256. Jeśli kopia się nie uda, nie rozpoczyna zmian. To nie jest backup całego Jellyfin ani biblioteki.
- Zadania czekają na swoją kolej. Pobieranie i aktualizacje mają po 90 minut i zapisują miejsce wznowienia; kolejne przebiegi nie zaczynają stale od tych samych pozycji.
- Historia pobrań zapisuje się partiami i ma kopię poprzedniej wersji. Błąd odczytu nie zeruje historii. Przerwaną podmianę można odzyskać po restarcie, a nieudane cofnięcie nie usuwa kopii pliku.
- W trybie wszystkich wersji wtyczka uzupełnia wydania dozwolonych grup, nawet jeśli odcinek ma już polskie napisy. Nowe pliki dostają rozróżniające końcówki; nazw wcześniej pobranych plików nie zmieniamy.
- Ręczne dopasowania mogą wskazywać sezon docelowy i przesunięcie numeracji. Pliki obejmujące kilka odcinków nadal wymagają ręcznego wyboru. Przeniesione pliki można ponownie powiązać po identyfikatorze Jellyfin i sumie kontrolnej.
- Pobieranie sprawdza, czy plik nie jest pusty, mieści się w limicie 32 MB i ma prawidłową sumę SHA-256. Limit czasu obejmuje także odczyt danych.
- W panelu jest do 30 raportów z ostatnich 30 dni, szczegóły problemów oraz pomiary czasu połączeń i zapisu historii.

Przed uznaniem tej wersji za stabilną potrzebne są testy na docelowym Jellyfin, w tym sezonów 2 i 3, oraz obserwacja dwóch nocnych przebiegów. Nie usuwaj historii pobrań ani plików odzyskiwania. Instrukcja: [przed aktualizacją do 0.7.2.0](https://github.com/i3lackhawk/jellyfin-plugin-azureillusion/blob/main/docs/UPGRADE_0.7.2.md).

## 0.7.1.0 - 2026-10-16

Ta wersja poprawia przede wszystkim aktualizowanie napisów w dużych bibliotekach i rozpoznawanie kolejnych sezonów.

- Sprawdzane są wszystkie napisy zarządzane przez wtyczkę, partiami do 250 pozycji. To rozmiar partii, a nie dzienny limit.
- Błąd lub wyłączenie pobierania nie blokuje osobno włączonych aktualizacji. Anulowanie zadania zatrzymuje oba etapy.
- Problemy z połączeniem nie są już opisywane jako brak napisów, a niepełna odpowiedź serwera nie oznacza usunięcia wydania. Starszy sposób sprawdzania wersji włącza się tylko wtedy, gdy nowa funkcja API jest niedostępna (404, 405 lub 501).
- Dopasowanie uwzględnia identyfikatory konkretnego odcinka lub sezonu, MAL, AniDB, Kitsu i oryginalne tytuły. Kolejne sezony nie dostają automatycznie identyfikatora całej serii.
- Można ręcznie wskazać zakres odcinków i przesunięcie numeracji. Nakładające się zakresy lub brak zakresu blokują automatyczny wybór.
- Zmiany grup i formularza zapisują się po kolei, na podstawie aktualnych ustawień z serwera.
- Napisy pobrane ręcznie trafiają do aktualizacji dopiero po potwierdzeniu pliku i sumy SHA-256. Wygaśnięcie niepotwierdzonego wpisu nie usuwa pliku.
- Diagnostyka łączy powtarzające się zdarzenia, rozróżnia wersje wtyczki i pokazuje osobno brak dopasowania oraz brak wyników. Pobierane pliki są sprawdzane pod kątem pustej zawartości, sumy SHA-256 i limitu 32 MB.

Przed pełnym skanowaniem dużej biblioteki zaktualizuj także WebSubs. Zbiorcze sprawdzanie wersji korzysta z `POST /api/public/v1/subtitles/revisions`; starsza strona wymaga wolniejszego trybu zgodności.

Z przygotowania wydania: przeszło 80 testów wtyczki i 33 testy powiązanej integracji strony; kompilacje i paczka zostały sprawdzone. Pełny zestaw strony miał wtedy 351 zaliczonych, 15 niezaliczonych i 2 pominięte testy. Nie było to potwierdzenie wdrożenia ani poprawnego działania na docelowym serwerze — po instalacji trzeba sprawdzić połączenie, zapis grup i pobieranie dla kolejnych sezonów.

## 0.7.0.2 - 2026-10-15

Poprawiliśmy mylący komunikat przy zapisie grup. Ustawienia były zapisane, ale panel Jellyfin potrafił odczytać ich starą kopię i pokazać błąd.

Potwierdzenie korzysta teraz z wyniku zapisu. Same ustawienia nadal przechowuje serwer Jellyfin.

## 0.7.0.1 - 2026-10-15

Po automatycznym zapisie grup wtyczka ponawia odczyt ustawień, żeby nie pokazywać błędu, gdy serwer nie zdążył jeszcze zwrócić nowej konfiguracji.

Poprawiliśmy też wyświetlanie starszych wpisów diagnostycznych — zamiast „undefined” pojawiają się właściwe dane.

## 0.7.0.0 - 2026-10-15

Uporządkowaliśmy automatyczne pobieranie, aktualizacje i zapis ustawień grup.

- Wtyczka zachowuje AniList ID konkretnego sezonu zamiast zastępować je identyfikatorem całej serii. Odcinki bez numeru są pomijane, bez wyszukiwania napisów dla całego sezonu.
- Pobieraniem automatycznym zajmuje się własne zadanie wtyczki. Ręczne wyszukiwanie w Jellyfin nadal działa.
- Brakujące napisy i aktualizacje są obsługiwane kolejno w jednym harmonogramie. Wersje są sprawdzane partiami do 250 plików, zaczynając od tych najdawniej sprawdzanych.
- Wybrane, ignorowane i priorytetowe grupy zapisują się automatycznie, z potwierdzeniem serwera.
- Poprawiliśmy daty i dane w raportach. Uszkodzona historia jest odkładana do osobnego pliku zamiast blokować całą wtyczkę.

To opis zachowania tej wersji. Nowsze wydania zmieniają sposób postępowania z uszkodzoną historią — przed aktualizacją sprawdź ich uwagi.

## 0.6.0.8 - 2026-10-14

Dodaliśmy domyślne uruchamianie pobierania brakujących napisów codziennie o 04:00.

Wcześniej zadanie mogło być widoczne w Jellyfin, ale nie mieć ustawionego terminu uruchomienia. Harmonogram można zmienić w panelu zadań.

## 0.6.0.7 - 2026-10-13

Poprawiliśmy rozpoznawanie sezonów, którym Jellyfin przypisał rok premiery całej serii.

Jeśli tytuł z rokiem nie daje wyniku, wtyczka próbuje bez roku. Dopasowanie przyjmuje tylko wtedy, gdy dokładny tytuł wskazuje jedno anime. Dodaliśmy testy dla błędnego roku i niejednoznacznych nazw.

## 0.6.0.6 - 2026-10-13

Poprawiliśmy dopasowanie serii podzielonych na sezony w Jellyfin.

Wtyczka sprawdza AniList ID sezonu. Identyfikator całej serii może pomóc tylko przy pierwszym sezonie; następne korzystają z własnej nazwy lub wariantów takich jak „Season 2”. Niepewne dopasowania są pomijane i widoczne w diagnostyce.

Zmianę obejmują testy serii wielosezonowych.

## 0.6.0.5 - 2026-10-02

Strona może teraz rozpoznać pełny numer zainstalowanej wersji wtyczki. Jest on przekazywany przy zapytaniach do API i podczas dodatkowego sprawdzenia co 30 minut.

W tej wersji domyślny harmonogram aktualizacji pozostaje ustawiony na 01:00, z limitem sześciu godzin.

## 0.6.0.4 - 2026-10-02

Dodaliśmy panel diagnostyki z licznikami i ostatnimi problemami, żeby łatwiej było ustalić, dlaczego napisy nie zostały pobrane.

Wtyczka lepiej dostosowuje tempo zapytań do strony i czeka tyle, ile serwer wskazuje po przekroczeniu limitu. Domyślne aktualizacje uruchamiają się o 01:00 i mogą pracować do sześciu godzin.

## 0.6.0.3 - 2026-10-02

Aktualizacje zaczynają się od napisów, które najdłużej czekają na sprawdzenie. Dzięki temu limit czasu nie powoduje codziennego wracania do tych samych plików z pominięciem reszty biblioteki.

Powtarzające się zapytania w jednym przebiegu są wykonywane tylko raz.

## 0.6.0.2 - 2026-10-01

Łatwiej dopasować wszystkie odcinki sezonu bez wpisywania każdego pliku osobno.

Odcinki pierwszego sezonu mogą korzystać z AniList ID serii. Ręczne przypisanie można powiązać z identyfikatorem serii i sezonu albo katalogiem nadrzędnym, tak aby objęło pasujące odcinki.

## 0.6.0.1 - 2026-10-01

Poprawiliśmy zapisywanie historii pobrań: wpis powstaje dopiero po zakończeniu pobierania, a błędy dysku nie powinny uszkadzać wcześniejszych danych.

Dodaliśmy też ręczne dopasowanie anime na podstawie identyfikatora lub ścieżki pliku.

## 0.6.0.0 - 2026-09-20

Przygotowaliśmy wtyczkę dla Jellyfin Server 12.1.0 i przenieśliśmy projekt oraz testy na .NET 10.

Zaktualizowaliśmy oznaczenie zgodności w katalogu oraz skrypty budowania i publikowania. To wydanie jest przeznaczone dla linii Jellyfin 12, nie starszego serwera 10.11.

## 0.5.4.0 - 2026-09-10

Naprawiliśmy pobieranie po znalezieniu napisów. Jellyfin dodawał do identyfikatora wyników oznaczenie dostawcy, którego wtyczka nie odczytywała poprawnie.

Zadanie automatyczne może teraz przekazać wynik do pobrania. Dodaliśmy test takiego identyfikatora.

## 0.5.3.0 - 2026-09-10

Ten sam plik wideo nie jest już sprawdzany kilka razy, gdy różne biblioteki Jellyfin wskazują ten sam katalog.

Wtyczka porównuje pełne, ujednolicone ścieżki plików i pomija powtórzenia.

## 0.5.2.0 - 2026-09-10

Skanowanie obejmuje wszystkie pliki wideo z wybranych bibliotek, także te, których Jellyfin nie oznaczył jako film lub odcinek.

Tak samo działa odnajdywanie wideo przy aktualizacji wcześniej pobranych napisów.

## 0.5.1.0 - 2026-09-10

Poprawiliśmy działanie przy dużych bibliotekach. Wtyczka wysyła zapytania spokojniejszym tempem, lepiej czeka po przekroczeniu limitu serwera i ponawia połączenie po przekroczeniu czasu.

Dopasowanie jednej serii jest współdzielone między jej odcinkami, więc nie trzeba go za każdym razem pobierać od nowa. Rozszerzyliśmy rozpoznawanie języka polskiego, a statystyki strony oznaczają te pobrania jako pochodzące z Jellyfin.

## 0.5.0.0 - 2026-07-30

Dodaliśmy symulację pobierania: możesz sprawdzić wynik zadania bez zapisywania i zmieniania plików napisów.

- Można ustawić kolejność preferowanych grup.
- Pobieranie, symulacja i aktualizacje mają raporty JSON dostępne dla administratora. Raporty są zapisywane tak, aby przerwany zapis nie zastępował poprzedniego wyniku niepełnym plikiem.
- Przed pobieraniem sprawdzane jest wolne miejsce, z ustawianą rezerwą i dodatkowym miejscem na pliki robocze.
- Aktualizacja zachowuje jedną poprzednią wersję z końcówką `.pna-backup`. Jeśli nie uda się zapisać historii, podmiana jest wycofywana.

Wydanie obejmuje też zmiany przygotowane w wersjach 0.4.x: ignorowanie grup, szczegółowe logi i aktualizowanie wcześniej pobranych napisów.

## 0.4.2.0 - 2026-07-30

Możesz wskazać kilka grup, z których nie chcesz pobierać napisów. Lista jest zwijana i ma wyszukiwarkę.

Ignorowanie ma pierwszeństwo przed wyborem grup i jest uwzględniane przed rankingiem oraz limitem wyników. Napisy tych grup nie są pobierane ani aktualizowane, ale istniejące pliki nie są usuwane.

## 0.4.1.0 - 2026-07-30

Dodaliśmy szczegółowe logi, które można włączyć w ustawieniach podczas szukania problemu.

Pokazują wyszukiwanie i pobieranie, czas odpowiedzi, kod HTTP oraz numer próby. Nie zapisują klucza API ani parametrów zapytań. Opcja jest domyślnie wyłączona.

## 0.4.0.0 - 2026-07-30

Jellyfin pokazuje nazwę grupy przy języku pobranych napisów. Dodaliśmy też zadanie sprawdzające, czy na stronie pojawiła się ich nowsza wersja.

Wtyczka zapamiętuje pobrane pliki i nie uruchamia pobierania oraz aktualizacji jednocześnie. Jeśli wydanie zniknie ze strony, lokalne napisy pozostają na miejscu. Dotychczasowa historia pobrań jest zachowana.

## 0.3.1.0 - 2026-07-29

Zaktualizowaliśmy biblioteki połączeń HTTP i narzędzia testowe.

Wersja nadal jest przeznaczona dla Jellyfin Server 10.11.11 i .NET 9. Kompilacja produkcyjna oraz 15 ówczesnych testów wtyczki przeszły bez błędów.

## 0.3.0.0 - 2026-07-28

Dodaliśmy własne zadanie Jellyfin „Polskie Napisy Anime — pobierz brakujące napisy”. Korzysta z bibliotek, języków i wyboru wersji ustawionych bezpośrednio we wtyczce.

- Napisy zapisuje standardowy mechanizm Jellyfin: obok wideo lub w katalogu metadanych, zgodnie z ustawieniami biblioteki i możliwością zapisu.
- Domyślnie dostępne są wszystkie grupy, również dodane później. Listę grup można przeszukiwać, przewijać i szybko zaznaczać.
- Niepewny numer sezonu jest przyjmowany tylko przy jednoznacznym wyniku. Logi rozróżniają brak dopasowania, brak wyników i błąd zapisu.
- Historia pobrań działa szybciej dzięki ograniczeniu powtórnych odczytów pliku. Pełna lista grup jest pobierana stronami; przygotowaliśmy też obsługę aliasów anime.

## 0.2.0.0 - 2026-07-22

Wtyczka otrzymała nazwę „Polskie Napisy Anime” i własne logo.

Możesz wybrać kilka bibliotek i języków oraz pobierać wszystkie wersje lub tylko jedną najlepszą. Test połączenia, grup i języków wykonuje serwer Jellyfin.

Usunęliśmy lokalne mapowanie identyfikatorów i doprecyzowaliśmy, jak działa dopasowanie po tytule i roku, gdy brakuje identyfikatora anime.

## 0.1.0.1 - 2026-07-21

Poprawiliśmy uruchamianie wtyczki na Jellyfin Server 10.11.11.

Operacje zapisu nie są już wykonywane podczas inicjalizacji. Usuwa to problem, przez który uruchamianie dodatku mogło zatrzymać serwer, zaobserwowany przy instalacji na NAS-ie.

## 0.1.0.0 - 2026-07-19

Pierwsze wydanie wtyczki AzureIllusion dla Jellyfin.

Można pobierać napisy ASS i SRT przez HTTPS, wybierać preferowane lub najlepiej oceniane grupy i korzystać z listy grup pobieranej ze strony. Wtyczka zapamiętuje pobrane wydania, żeby nie pobierać ponownie tego samego pliku.

Ta wersja zawierała lokalne przypisania Kitsu i AniDB do AniList. Udostępniliśmy też katalog Jellyfin i automatyczne publikowanie paczek na GitHubie.
