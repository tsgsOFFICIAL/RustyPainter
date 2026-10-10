# Ændringslog

Alle væsentlige ændringer i Rusty Painter er dokumenteret her.

## 0.9.0

### Animerede neonskilte

- De tre animerede neonskilte kan males, med op til fem frames. Hvert frame har sit eget billede og sin egen placering på lærredet, og et nyt frame starter som en kopi af det åbne.
- Studio afspiller frames som en forhåndsvisning i tre hastigheder: langsom viser et frame hvert andet sekund, mellem hvert sekund og hurtig to gange i sekundet.
- Billedet over spillet går også gennem frames i den hastighed, du valgte.
- Malingen åbner hvert frame efter tur, rydder det og maler det. Frames efter det sidste billede ryddes også, så der ikke er noget gammelt tilbage. Hvis et frame ikke vil åbne, stopper malingen og fortæller det.
- Gemte projekter beholder alle deres frames.

### Om

- En ny Om-side viser versionen, byggetidspunktet, operativsystemet og licensen, så en fejlrapport har det, den skal bruge.
- Den åbner ændringsloggen i sit eget vindue, med billeder og animerede GIF'er, og kan søge efter opdateringer.
- Den har knapper til GitHub, Discord og Ko-fi.
- Ændringsloggen og noterne, der vises ved en opdatering, findes på engelsk, dansk og russisk. En version, der endnu ikke er oversat, vises på engelsk.

### Layout

- Hver side blev tjekket i flere vinduesstørrelser, på alle tre sprog og i begge udseender. Det mindste vindue er nu 1240 pixel bredt, fordi Studio skal bruge så meget plads til sin forhåndsvisning.
- Rettet Studios overskrift, der pressede skiltets navn ned til nogle få bogstaver, lange navne, der blev skåret af, farven på statuslinjen, knapper, der var svære at læse i det lyse tema, og et søgefelt, der skar sin tekst af.

## 0.8.0

### Sprog og udseende

- Appen taler engelsk, dansk og russisk. Den starter på Windows' sprog, og sproget kan skiftes med det samme i Indstillinger, med et flag for hvert.
- Et lyst tema ved siden af det mørke. Det kan følge Windows eller indstilles manuelt i Indstillinger.
- Ord, der afhænger af et tal, som "1 streg" og "5 streger", har den rigtige form på hvert sprog, og tal og datoer skrives, som hvert sprog skriver dem.

## 0.7.0

### Indstillinger

- En større Indstillinger-side. Starthastigheden, billedet over spillet, hvor massivt det er, statusboksen og maletasterne kan alle indstilles én gang og bruges hver gang.
- En knap åbner mappen, hvor biblioteket gemmer sine projekter, og en knap sætter maleindstillingerne tilbage, som de var i starten.
- Kontakter og skydere bruger appens orange.

## 0.6.0

### Bibliotek

- Studio kan gemme alt om et billede: overfladen, placeringen, farverne og penslen. Når du åbner det igen, får du det samme resultat.
- Biblioteksiden viser de gemte projekter som kort med et lille billede af resultatet. Du kan søge, åbne, omdøbe, kopiere og slette dem, og en sletning spørger først.
- Projekter gemmes som almindelige mapper, så de er nemme at sikkerhedskopiere eller flytte til en anden computer.

## 0.5.0

### Over spillet

- Billedet kan vises over spillet, hvor det bliver malet, så placeringen kan tjekkes, før noget males. Hvor massivt det er, kan ændres. Det tager aldrig et klik eller fokus.
- En lille boks øverst i spillet viser, hvor langt malingen er kommet, så appen ikke behøver at være synlig.
- Sidefoden fortæller, om Rust kører.
- Maletasterne virker kun, mens Rust er det forreste vindue, så intet andet program mister en tast.

## 0.4.0

### Maling

- Studio maler billedet ind i Rust. Maleren gør malerskærmen klar, vælger hver farve og maler stregerne med musen og tastaturet.
- Felter med samme farve males som ét træk, langs en række eller ned ad en kolonne, alt efter hvad der er længst. Enkelte felter klikkes.
- En klar-side viser, hvad der skal males, hvor lang tid det tager, og hvad der skal gøres i spillet, og malesiden viser fremskridtet.
- Mellemrum starter, sætter på pause og genoptager. Esc stopper. Begge kan ændres i Indstillinger. Hvis du flytter musen eller forlader spillet, sættes der også på pause, og der sendes intet til spillet, før Rust er forrest.
- Lærredet kan ryddes først, hastigheden kan vælges, og placeringen kan finjusteres.
- En valgfri baggrundsfarve fylder de dele af billedet, der ellers ville blive tomme.

## 0.3.0

### Studio

- En ny Studio-side. Indlæs et billede ved at trække det ind, gennemse, indsætte det eller indsætte et link til det, eller skriv en tekst og lav et billede af den, med egne farver til bogstaverne og baggrunden.
- Placér billedet på lærredet med tilpas, fyld eller stræk. Zoom, flyt, roter og vend det, med skyderne eller ved at trække og bruge hjulet på lærredet.
- Farver tilpasses spillets palet eller alle farver, med op til 256 farver, glat eller mønstret dithering og filtre: sort-hvid, sepia, inverter, lysere og posteriser. Lysstyrke, kontrast og mætning kan justeres.
- Penselstørrelsen kan vælges, fra fin til grov, og neonskiltene har deres fire faste pensler.
- Resultatet vises, som det bliver malet, med en indikator, mens det laves. En delt visning sætter originalen ved siden af, med en skillelinje, du kan flytte.
- Antallet af streger, de brugte farver og en omtrentlig tid vises.

## 0.2.0

### Overflader

- Tretten overflader kan males: fire træskilte, fem kunstnerlærreder, en portrætramme og tre neonskilte. Størrelsen og placeringen af hvert lærred blev målt i spillet.
- Appen finder spilvinduet og virker i enhver 16:9-opløsning.
- Begge af spillets farvetilstande understøttes: dets palet og en vilkårlig farve skrevet som hex-kode.
- Lærreder, der åbner zoomet ind, zoomes ud for dig.

## 0.1.1

### Opdateringer

- Testudgave, der skal vise, at en installeret 0.1.0 kan opdatere sig selv. Der er ingen andre ændringer.

## 0.1.0

### Appen

- Første build med sidebjælken, en startside og en side med indstillinger.

### Opdateringer

- Appen leder efter en nyere version på GitHub, når den starter, og viser, hvad der er ændret.
- Den kan selv hente og installere den nye version. Installerede kopier kører det nye installationsprogram, bærbare kopier bytter deres filer, og begge genstarter bagefter.
- Overførsler prøves igen ved en dårlig forbindelse og kontrolleres mod den kontrolsum, GitHub udgiver. Hvis en opdatering mislykkes, bliver din installerede version, som den var.
