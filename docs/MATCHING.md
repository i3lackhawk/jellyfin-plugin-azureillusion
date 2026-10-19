# Dopasowanie sezonów i odcinków

Jellyfin może przechowywać kilka sezonów pod jedną serią, podczas gdy AniList
traktuje je jako osobne anime. Wtyczka rozpoznaje więc konkretny odcinek i sezon,
a nie tylko nazwę całej serii.

## Kolejność dopasowania

1. Ręczne przypisanie zapisane w ustawieniach wtyczki.
2. Identyfikatory odcinka lub sezonu, w tym AniList i obsługiwane identyfikatory innych dostawców metadanych.
3. Identyfikator serii nadrzędnej — tylko jako pomoc dla pierwszego sezonu o ogólnej nazwie.
4. Dokładny tytuł lub alias, jeśli włączono dopasowanie bez AniList ID.

Nie zakładaj, że dostawca metadanych zapisze osobny AniList ID przy każdym sezonie.
Kolejne sezony nie dziedziczą automatycznie identyfikatora pierwszego.

Przy dopasowaniu po tytule wtyczka może ponowić wyszukiwanie bez roku, ponieważ
Jellyfin czasem przypisuje sezonowi rok premiery całej serii. Nadal potrzebny
jest jednoznaczny wynik. Podobny tytuł sam w sobie nie wystarczy.

## Sprawdzenie bez pobierania

W ustawieniach wpisz ścieżkę pliku widoczną dla serwera Jellyfin i kliknij
**Sprawdź dopasowanie bez pobierania**. Podgląd korzysta z zapisanych ustawień,
więc najpierw zatwierdź ich zmiany.

## Ręczne dopasowanie

Pole **Ręczne dopasowania AniList** przyjmuje JSON. Przykładowe identyfikatory
poniżej trzeba zastąpić własnymi.

Jeśli jeden sezon Jellyfin zawiera dwie części z AniList, podaj osobne zakresy:

```json
{
  "id:IDENTYFIKATOR_SERII_JELLYFIN#season:1": [
    {"anilistId":"12345","episodeStart":1,"episodeEnd":12,"episodeOffset":0},
    {"anilistId":"67890","episodeStart":13,"episodeEnd":24,"episodeOffset":-12}
  ]
}
```

W drugim zakresie odcinek 13 z Jellyfin odpowiada odcinkowi 1 drugiego anime:
`13 + (-12) = 1`.

Możesz również wskazać konkretny plik:

```json
{
  "path:/media/anime/odcinek.mkv": 12345,
  "path:/media/anime/odcinek.mkv#season:2": 67890
}
```

Obsługiwane są też klucze dostawców, np. `anidb:123`.
Dla dalszych sezonów użyj klucza `#season:N` lub klucza konkretnego elementu.

Zakres może zawierać `targetSeason` (sezon docelowy) oraz `episodeOffset`
(przesunięcie numeracji). Wtyczka nie zgaduje przesunięcia z samego numeru sezonu.

Nakładające się zakresy albo brak zakresu dla odcinka blokują automatyczny wybór.
Pliki wideo obejmujące kilka odcinków wymagają ręcznego wyboru napisów.

[Powrót do instrukcji](../README.md)
