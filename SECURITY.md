# Bezpieczeństwo

## Zgłaszanie podatności

Nie publikuj kluczy API, logów zawierających dane uwierzytelniające ani opisu
aktywnej podatności w publicznym zgłoszeniu GitHub. Zgłoszenie należy przekazać
prywatnym kanałem administratorowi AzureIllusion.

## Klucze API

- Każda instalacja Jellyfin powinna mieć osobny klucz.
- Klucz należy ograniczyć do niezbędnych uprawnień i limitu zapytań.
- Klucza nie wolno dodawać do kodu, `manifest.json`, zrzutów ekranu ani logów.
- Po podejrzeniu wycieku klucz trzeba unieważnić i utworzyć nowy.

## Łańcuch wydania

GitHub Actions buduje paczkę bez sekretów aplikacji. Katalog zawiera sumę MD5
wymaganą przez Jellyfin, a obok wydania publikowana jest suma SHA-256 do
niezależnej weryfikacji pliku.
