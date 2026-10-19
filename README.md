# Polskie Napisy Anime dla Jellyfin

Oglądaj anime z polskimi napisami dostępnymi na AzureIllusion. Wtyczka wyszukuje
napisy do filmów i odcinków w Twojej bibliotece, pobiera wybrane wersje i sprawdza,
czy pojawiły się ich aktualizacje.

Wybierasz biblioteki, języki i grupy tłumaczeniowe. Możesz pobierać wszystkie
pasujące wersje albo tylko jedną, z uwzględnieniem ulubionych grup.

## Instalacja

Wtyczkę dodajesz przez panel Jellyfin. Sposób instalacji jest taki sam niezależnie
od tego, czy serwer działa na komputerze, NAS-ie czy w kontenerze.

1. Zaloguj się do Jellyfin jako administrator.
2. Otwórz `Panel administracyjny > Wtyczki` i ustawienia repozytoriów.
3. Dodaj repozytorium o nazwie `Polskie Napisy Anime`, podając adres:

   ```text
   https://github.com/i3lackhawk/jellyfin-plugin-azureillusion/raw/refs/heads/catalog/manifest.json
   ```

4. W katalogu wtyczek znajdź **Polskie Napisy Anime** i wybierz instalację.
5. Uruchom ponownie Jellyfin.

Potrzebujesz zgodnej wersji serwera i dostępu do internetu.
Ten kod jest przygotowany dla **Jellyfin Server 12.1.0**. Katalog może zawierać
wydania dla różnych wersji serwera — wybierz zgodne ze swoją instalacją.

Pełna instrukcja i pomoc przy instalacji: [Instalacja w Jellyfin](docs/INSTALL.md).

## Pierwsze uruchomienie

1. Otwórz ustawienia wtyczki w panelu Jellyfin.
2. Zostaw adres API `https://subs.azureillusion.ovh` i wklej klucz utworzony na stronie AzureIllusion. Najlepiej użyć osobnego klucza tylko do Jellyfin.
3. Kliknij **Sprawdź połączenie**.
4. Wybierz biblioteki, języki i grupy. Domyślnie wtyczka korzysta ze wszystkich bibliotek i grup.
5. Zdecyduj, czy chcesz pobierać wszystkie pasujące wersje, czy tylko jedną. Zapisz ustawienia.
6. W ustawieniach odpowiednich bibliotek włącz dostawcę napisów **Polskie Napisy Anime**.

Ustawienia grup zapisują się automatycznie. Pozostałe zmiany zatwierdź przyciskiem
**Zapisz**. Przed pierwszym pobieraniem możesz uruchomić zadanie
**Polskie Napisy Anime — symuluj pobieranie**: sprawdzi dopasowania bez pobierania napisów.

## Jak działa pobieranie?

Wtyczka ma własne zadanie pobierania i aktualizacji napisów. Domyślnie uruchamia
się codziennie o 04:00; harmonogram zmienisz w panelu zadań Jellyfin. Napisy możesz
też wyszukać ręcznie z poziomu odcinka lub filmu.

Możesz wykluczyć wybrane grupy albo ustawić ich kolejność. Ignorowane grupy nie są
pobierane ani aktualizowane, a wcześniej zapisane pliki pozostają na miejscu.
W trybie jednej wersji pierwszeństwo mają grupy ustawione wyżej na liście priorytetów.

O miejscu zapisu decydują ustawienia biblioteki Jellyfin. Napisy trafiają obok
wideo lub do katalogu metadanych. Wtyczka obsługuje ASS i SRT, filmy, seriale
oraz odcinki specjalne.

## Seria i sezony — dlaczego czasem trzeba pomóc?

Jellyfin zwykle pokazuje jedną serię z kilkoma sezonami, a AniList może mieć
osobny wpis dla każdego z nich. Dlatego dopasowanie pierwszego sezonu nie
wystarcza do rozpoznania kolejnych.

Wtyczka korzysta z zapisanych identyfikatorów i ręcznych dopasowań. Jeśli ich
brakuje, może rozpoznać sezon po dokładnym tytule lub aliasie. Gdy wynik jest
niejednoznaczny, pomija pobieranie i pokazuje przyczynę w diagnostyce — nie wybiera
przypadkowej serii.

Pomoc przy nietypowej numeracji i ręcznym przypisaniu:
[Dopasowanie sezonów i odcinków](docs/MATCHING.md).

## Gdy coś nie działa

Najpierw sprawdź połączenie w ustawieniach wtyczki, a potem zajrzyj do
**Diagnostyki dodatku**. Zobaczysz tam m.in. brak dopasowania, problemy z API
i wyniki ostatnich zadań. Stare zdarzenie w historii nie musi oznaczać,
że problem nadal występuje.

Nie udostępniaj klucza API na zrzutach ekranu ani w zgłoszeniach.
Jeśli klucz trafił do kogoś innego, unieważnij go na stronie i utwórz nowy.
Więcej: [Bezpieczeństwo](SECURITY.md).

## Aktualizacje i dokumentacja

- [Instalacja i rozwiązywanie problemów](docs/INSTALL.md)
- [Dopasowanie sezonów i odcinków](docs/MATCHING.md)
- [Informacje przed aktualizacją do wersji testowej 0.7.2.0](docs/UPGRADE_0.7.2.md)
- [Historia zmian](CHANGELOG.md)

Wersja 0.7.2.0 pozostaje wydaniem testowym. Nie oznaczamy jej jako stabilnej bez
testów na docelowym serwerze i obserwacji automatycznych zadań.

## Dla osób rozwijających wtyczkę

Do budowania potrzebny jest .NET 10 SDK. Lokalne testy i przygotowanie paczki:

```powershell
.\prepare-release.ps1 -GitHubOwner i3lackhawk
```

Skrypt uruchamia testy, buduje paczkę i sprawdza katalog oraz sumy kontrolne.
Zasady publikacji i utrzymania opisują
[wydawanie na GitHub](docs/GITHUB_RELEASE.md) oraz [utrzymanie wersji](docs/MAINTENANCE.md).

## Licencja

GPL-3.0-or-later. Zobacz [LICENSE.md](LICENSE.md).
