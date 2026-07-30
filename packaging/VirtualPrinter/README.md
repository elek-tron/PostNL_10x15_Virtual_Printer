# Windows 11 virtuele printer

Deze map bevat het manifest- en printercapabilities-ontwerp voor de latere
printerwachtrij `PostNL 10x15`.

De wachtrij ontvangt PDF waar mogelijk rechtstreeks van Edge/Chrome en OXPS
van andere Windows-programma's. Er is bewust geen `OutputFileTypes`-attribuut:
daardoor wordt dit geen "Opslaan als"-printer. De achtergrondtaak geeft de
gegevens door aan de lokale worker, die ze bij PDF24 of de Zebra aflevert.

Installatie is nog niet geactiveerd. Eerst worden uitsnede, orientatie en
barcodekwaliteit met PDF24 gecontroleerd.

