# Instalacja w Jellyfin

Wtyczkę instalujesz z katalogu Jellyfin, bez ręcznego kopiowania plików DLL.
Instrukcja nie zależy od systemu, na którym działa serwer.

## Zanim zaczniesz

Potrzebujesz konta administratora Jellyfin, dostępu serwera do GitHub i strony
AzureIllusion oraz osobnego klucza API utworzonego na AzureIllusion.

Sprawdź wersję Jellyfin w panelu administracyjnym. To wydanie jest budowane dla
Jellyfin Server 12.1.0. Nie instaluj go na innej wersji bez potwierdzenia zgodności.
Numer wersji wtyczki i numer serwera to dwie różne rzeczy.

## Dodaj repozytorium

1. Otwórz `Panel administracyjny > Wtyczki`.
2. Przejdź do ustawień repozytoriów i dodaj nowe.
3. Wpisz nazwę `Polskie Napisy Anime` oraz adres:

   ```text
   https://github.com/i3lackhawk/jellyfin-plugin-azureillusion/raw/refs/heads/catalog/manifest.json
   ```

4. Zapisz i odśwież katalog wtyczek.
5. Znajdź **Polskie Napisy Anime**, wybierz zgodne wydanie i zainstaluj.
6. Uruchom ponownie serwer Jellyfin w sposób właściwy dla swojej instalacji.

Nazwy zakładek mogą się nieznacznie różnić między wersjami panelu.
Podaj adres `manifest.json`, nie link do paczki ZIP.

## Połącz z AzureIllusion

1. Otwórz ustawienia zainstalowanej wtyczki.
2. W polu **Adres API** wpisz `https://subs.azureillusion.ovh`.
3. Wklej osobny klucz API i kliknij **Sprawdź połączenie**.
4. Wybierz biblioteki, języki, grupy oraz liczbę pobieranych wersji.
5. Zatwierdź formularz przyciskiem **Zapisz**. Ustawienia grup zapisują się także automatycznie.
6. W ustawieniach bibliotek włącz dostawcę napisów **Polskie Napisy Anime**.

Najpierw sprawdź jedno anime. Możesz ręcznie wyszukać napisy przy odcinku albo
uruchomić zadanie **Polskie Napisy Anime — symuluj pobieranie**, które nie pobiera
napisów. Przy kilku sezonach sprawdź każdy z nich osobno.

## Aktualizacja

Po udostępnieniu zgodnego wydania i odświeżeniu katalogu aktualizacja będzie
dostępna w Jellyfin. Po jej instalacji uruchom ponownie serwer.

Nie usuwaj konfiguracji ani historii pobrań, żeby „odświeżyć” wtyczkę.
Przed instalacją wersji testowej przeczytaj
[informacje o aktualizacji do 0.7.2.0](UPGRADE_0.7.2.md).

## Najczęstsze problemy

- **Nie ma wtyczki w katalogu.** Sprawdź adres repozytorium. Powinien zwracać JSON, a nie stronę błędu. Sprawdź też dostęp serwera Jellyfin do GitHub.
- **Wtyczka jest oznaczona jako niezgodna.** Wybierz wydanie przeznaczone dla swojej wersji Jellyfin; nie wymuszaj instalacji.
- **Test połączenia nie przechodzi.** Sprawdź adres API, ważność klucza oraz połączenie serwera z AzureIllusion. To serwer Jellyfin wykonuje zapytania, nie telefon z otwartym panelem.
- **Nie znaleziono napisów.** Sprawdź wybrane języki i grupy oraz diagnostykę dopasowania. Brak wyników może oznaczać zarówno brak wydania na stronie, jak i nierozpoznany sezon.
- **Pierwszy sezon działa, a następne nie.** Zajrzyj do [instrukcji dopasowania sezonów](MATCHING.md). Kolejny sezon nie może korzystać automatycznie z identyfikatora pierwszego.
- **Po aktualizacji widzisz starą wersję.** Sprawdź, czy ponownie uruchomiony został serwer Jellyfin, a nie tylko aplikacja odtwarzacza.

[Powrót do opisu wtyczki](../README.md)
