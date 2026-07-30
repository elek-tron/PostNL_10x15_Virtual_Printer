# Architectuur

## Doel

In het Windows-afdrukvenster komt een printer `PostNL 10x15`. Een afdruk naar
die wachtrij:

1. ontvangt PDF rechtstreeks van een browser, of OXPS van een ander programma;
2. zoekt de grote getekende labelrand met verhouding 3:2;
3. weigert veilig wanneer geen eenduidig label wordt gevonden;
4. maakt een pagina van exact 150 x 100 mm;
5. rendert die op 8 dots/mm naar 1200 x 800 zwart-wit geschikte pixels;
6. draait die via Windows liggend op een fysieke rolmaat van 100 x 150 mm;
7. stuurt het resultaat naar de ingestelde Windows-printer.

## Onderdelen

```text
Browser / PDF-lezer
        |
        v
Windows-wachtrij "PostNL 10x15"
        |
        v
MSIX Print Support Virtual Printer-endpoint
        |
        v
PostNL10x15.Worker
  - detecteren
  - vectorieel uitsnijden
  - 1200 x 800 renderen
        |
        +--> PDF24 (test)
        |
        `--> Zebra ZD220 203 dpi (definitief)
```

De worker bevat geen e-mail- of PostNL-webpaginalogica. Daardoor blijft deze
route werken wanneer PostNL de e-mail of webpagina verandert, zolang de
afgedrukte pagina een herkenbare 10x15-labelrand bevat.

## Gefaseerde ingebruikname

1. Uitsnede en voorbeeld-PNG controleren.
2. Expliciet naar PDF24 printen.
3. Zebra-driver installeren en exacte Windows-printernaam invullen.
4. MSIX-endpoint bouwen, ondertekenen en lokaal installeren.
5. Wachtrij `PostNL 10x15` als enige gebruikerskeuze testen.

De eerste twee fasen zijn nu in het project uitvoerbaar. De MSIX-map is een
installatieprototype en installeert uit zichzelf niets.
