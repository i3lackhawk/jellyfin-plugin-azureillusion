# Instalacja w Jellyfin na Synology

Ta metoda nie wymaga dostępu do katalogu wtyczek ani ręcznego kopiowania DLL.
Jellyfin pobiera paczkę z GitHub Releases przez własny katalog wtyczek.

## Wymagania

- Jellyfin Server 10.11.11,
- połączenie Synology z `github.com` i `raw.githubusercontent.com`,
- publiczne repozytorium GitHub z opublikowanym wydaniem,
- osobny klucz API AzureIllusion.

Wersję serwera sprawdź w `Panel administracyjny > Informacje ogólne`. Pierwsze
wydanie wtyczki ma `targetAbi` równy `10.11.11.0`. Jeżeli pakiet Synology używa
innej wersji Jellyfin, przed instalacją trzeba zbudować zgodne wydanie wtyczki;
Jellyfin prawidłowo ukryje albo oznaczy niezgodną paczkę.

## Dodanie repozytorium

1. Zaloguj się do Jellyfin jako administrator.
2. Otwórz `Panel administracyjny`.
3. Przejdź do `Wtyczki` i otwórz ustawienia repozytoriów.
4. Dodaj repozytorium o nazwie `AzureIllusion`.
5. Wklej adres:

   ```text
   https://raw.githubusercontent.com/i3lackhawk/jellyfin-plugin-azureillusion/catalog/manifest.json
   ```

6. Zapisz i odśwież katalog wtyczek.
7. W kategorii `Metadata` wybierz `AzureIllusion` i kliknij `Zainstaluj`.
8. Uruchom ponownie pakiet Jellyfin z DSM albo z panelu Jellyfin.

Nazwy pozycji mogą się nieznacznie różnić zależnie od wersji językowej
Jellyfin. Nie wpisuj adresu do ZIP-a. Jellyfin potrzebuje adresu do
`manifest.json`.

## Konfiguracja po instalacji

1. Otwórz stronę wtyczki AzureIllusion.
2. Ustaw `https://subs.azureillusion.ovh` jako adres API.
3. Wklej klucz API przeznaczony dla Jellyfin.
4. Użyj testu połączenia.
5. Odśwież listę grup i wybierz strategię pobierania.
6. Włącz AzureIllusion jako dostawcę napisów w odpowiedniej bibliotece.

## Aktualizacje

Nowa wersja pojawi się w katalogu automatycznie po opublikowaniu tagu GitHub.
Po instalacji aktualizacji uruchom ponownie Jellyfin. Konfiguracja jest
przechowywana przez Jellyfin niezależnie od plików DLL.

## Diagnostyka

- Brak wtyczki w katalogu: otwórz adres manifestu w przeglądarce i sprawdź,
  czy zwraca JSON bez błędu 404.
- `Unsupported`: wersja `targetAbi` nie odpowiada wersji serwera Jellyfin.
- Błąd pobrania: sprawdź DNS, certyfikat HTTPS i dostęp Synology do GitHub.
- Brak wyników: sprawdź klucz API, identyfikator AniList i ustawienia grup.
- Po aktualizacji nadal stara wersja: uruchom ponownie pakiet Jellyfin.
