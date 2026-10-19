# Przed aktualizacją do 0.7.2.0

To wersja testowa. Przed użyciem na głównym serwerze sprawdź ją na kopii
biblioteki. Nie zastępuj opublikowanej paczki inną paczką z tym samym numerem wersji.

## Co zmienia się dla użytkownika?

- Możesz sprawdzić dopasowanie bez pobierania oraz podejrzeć postęp i historię zadań.
- Zmiana ustawień w drugiej karcie nie powinna po cichu nadpisać nowszych ustawień.
- Zajęte zadanie trafia do kolejki zamiast zostać pominięte.
- Tryb wszystkich wersji uzupełnia wydania dozwolonych grup, także gdy odcinek ma już polskie napisy.
- Ręczne dopasowania obsługują sezon docelowy i przesunięcie numeracji. Szczegóły: [dopasowanie sezonów](MATCHING.md).

Pełna lista zmian znajduje się w [historii wydań](../CHANGELOG.md).

## Kopia ustawień i historii

Przy pierwszym uruchomieniu nowa wersja tworzy na serwerze kopię konfiguracji,
historii pobrań, jej poprzedniej kopii oraz dzienników podmiany plików.
Zapisuje ją w sąsiednim katalogu `AzureIllusion-backups/0.7.2.0`
i sprawdza sumy SHA-256.

Ponowne uruchomienie nie zastępuje tej pierwszej kopii. Jeśli jej utworzenie się
nie uda, wtyczka nie rozpoczyna zmian danych. Kopia pozostaje lokalna, ale może
zawierać poufną konfigurację — nie udostępniaj jej publicznie.

**To nie jest kopia całego Jellyfin ani napisów zapisanych obok wideo.**
Własny backup serwera i biblioteki nadal jest potrzebny.

Nie usuwaj plików `downloads.json`, ich kopii ani katalogu `transactions`
podczas aktualizacji. Są potrzebne do zachowania historii i odzyskiwania
przerwanych podmian. Administrator może sprawdzić potwierdzenie kopii przez
`Plugins/PolskieNapisyAnime/upgrade-backup`.

## Zadania i pobierane pliki

Pobieranie i aktualizacje mają osobne limity czasu po 90 minut i zapisują miejsce
wznowienia. Kolejne wyszukiwanie następuje po 24 godzinach, a po błędzie po
15 minutach. Sprawdzenia historii są zapisywane partiami; nieczytelna historia
zatrzymuje zadanie zamiast zostać wyzerowana.

Wtyczka odrzuca puste pliki, pliki większe niż 32 MB i pliki z niezgodną sumą
SHA-256. Limit czasu obejmuje również odczyt pliku, nie tylko oczekiwanie na
odpowiedź serwera. Ręcznie zmienione napisy i kopie potrzebne do odzyskania
przerwanej aktualizacji są chronione.

Plik pobrany ręcznie trafia do historii aktualizacji po potwierdzeniu jego
lokalnej ścieżki i sumy SHA-256 podczas zadania konserwacji. Niepotwierdzony wpis
oczekujący wygasa po 24 godzinach; sam plik nie jest usuwany.

## Co sprawdzić po instalacji?

Sprawdź zapis ustawień, ręczne pobranie i dopasowanie kilku sezonów tej samej
serii. Następnie skontroluj harmonogram — stary wyzwalacz osobnego zadania
aktualizacji może nadal być aktywny.

Przed uznaniem wersji za stabilną potrzebne są testy na docelowym Jellyfin
i obserwacja dwóch nocnych przebiegów. Porównaj raporty zadań z plikami w bibliotece.

[Powrót do instrukcji](../README.md)
