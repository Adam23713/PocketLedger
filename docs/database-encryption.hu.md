# Adatbázis-titkosítás (PL-93)

## Hatókör és garanciák

A PocketLedger mostantól titkosítja a kiválasztott mezőket, mielőtt az EF Core kiírná őket a PostgreSQL-be. Mindhárom host külön ASP.NET Core Data Protection kulcskarikát használ, amelyek a PostgreSQL-en kívül vannak tárolva. Az éles környezet meglévő kulcskönyvtárat és RSA privát tanúsítványt igényel; a kulcskarika ezzel a tanúsítvánnyal van titkosítva. A PostgreSQL-konténerek soha nem csatolnak kulcsokat vagy tanúsítványokat. Nincs éles kapcsoló ennek a védelemnek a kikapcsolására, és nincs plaintext (nyílt szöveges) visszaesés visszafejtési hiba esetén.

A mezőszintű titkosítás önmagában **nem** titkosítja a teljes adatbázist. Az összegek, dátumok, kapcsolatok, hitelesítési keresőmezők és az adatbázis metaadatai olvashatók maradnak egy adatbázis-másolatban. Amíg az alábbi LUKS szakasz nincs befejezve, aki hozzáfér a teljes VPS lemezhez, hozzájuthat mind a titkosított szöveghez, mind a tanúsítvány privát kulcsaihoz. Egy külön könyvtár vagy Docker bind mount nem véd a teljes lemez ellopása ellen.

A LUKS az offline blokkeszközt védi, beleértve a fájlrendszeren tárolt PostgreSQL táblákat, indexeket, WAL-t és ideiglenes adatbázisfájlokat. A már feloldott fájlrendszeren keresztül történő fájlmásolás, az érvényes hitelesítő adatokkal végzett SQL-hozzáférés, a futó VPS-hez való root-hozzáférés, a memóriahozzáférés és a rosszindulatú adatbázis-írások kívül esnek ezen az offline lopás elleni garancián. A szolgáltató/host plaintext lemezekről vagy futó VM memóriájáról készített pillanatképeit külön kell kezelni. A mezővédelem célja az alkalmazások és mezők szétválasztása; nem köti a titkosított szöveget egy adott sorhoz, és nem akadályozza meg egy régebbi érték visszajátszását (replay).

A tranzakcióimporthoz elfogadott CSV-fájlok nyílt szövegesek. A tranzakcióexport jelszóval védett `.xlsx` munkafüzetet, a teljes pénzügyi mentés és visszaállítás pedig jelszóval védett `.plbackup` formátumot használ. Az adatbázis-titkosítás és az exportált fájlok titkosítása egymástól független védelmek.

## Adatosztályozás

| Adat | Védelem és indoklás |
| --- | --- |
| `Transaction.Note` (a jegyben `Description`) | Alkalmazásszintű titkosítás. Felhasználó által megadott érzékeny szöveg. |
| `RecurringTransaction.Note`, `PlannerItem.Note` | Alkalmazásszintű titkosítás; ezek tranzakciós jegyzeteket másolhatnak vagy generálhatnak. |
| `Account.Name`, `Category.Name` | Alkalmazásszintű titkosítás; a nevek személyes adatokat árulhatnak el. A rendezés a visszafejtés után történik. |
| `Debt.Name`, `Debt.CounterpartyName`, `Debt.Note` | Alkalmazásszintű titkosítás. |
| `UserPreference.DisplayName` | Alkalmazásszintű titkosítás. |
| `PlannerMonthRecord.SnapshotJson`, `OpeningBalancesJson` | A teljes tárolt payload titkosítása. A pillanatképek neveket, jegyzeteket, havi Notes-okat és pénzügyi előzményeket tartalmaznak. Egyetlen SQL JSON lekérdezés sem függ ezektől a payloadoktól. |
| Összegek, nyitóegyenlegek, pénznem, tranzakció dátuma/időpontja/típusa, azonosítók, tulajdonos-azonosítók, kapcsolatok | Tárolási (storage) titkosítás. A típusos, indexelt oszlopok megtartása megőrzi a szűrőket, joinokat, egyenlegszámításokat és a riportolást. A randomizált mezőtitkosítás megakadályozná a hasznos SQL összehasonlításokat/aggregációkat, és sokkal több adat betöltését igényelné. |
| Ikonok, színek, megjelenítési sorrend, jelzők (flag), időzóna és pénznem-formátum beállítások | Tárolási titkosítás; alacsony érzékenységű megjelenítési/konfigurációs adatok. |
| Identity `AspNetUserTokens.Value` | Alkalmazásszintű titkosítás, beleértve a meglévő Identity hitelesítő (authenticator) kulcsot és a helyreállítási kód reprezentációját. Az Identity token-generálása/validálása változatlan marad. |
| Identity telefonszám, utolsó sikeres bejelentkezés IP-je, audit remote/forwarded IP, user agent, metaadat | Alkalmazásszintű titkosítás. |
| Identity felhasználónevek, normalizált felhasználónevek/e-mailek, e-mail, felhasználói azonosítók; audit felhasználónév, időbélyeg, kimenetel, kérés útvonala, korrelációs/session ujjlenyomatok | Tárolási titkosítás. A felhasználónév/e-mail alapú keresés és az indexelt biztonsági esemény-lekérdezések kompatibilisek maradnak. Ha csak az egyik másolatot titkosítanánk, a normalizált keresőmásolatot pedig nem, az nem rejtené el az identitást. Nincs bevezetve blind-index séma. |
| Jelszó-hash-ek, Identity/OpenIddict kliens titok (client-secret) hash-ek | A meglévő egyirányú keretrendszerbeli hashelés plusz tárolási titkosítás. Soha nem cserélődik le visszafejthető jelszó-titkosításra. |
| Web BFF munkamenet, beleértve az access/refresh tokeneket | A meglévő Data Protection titkosítás; a kulcsok most már az adatbázisán kívül élnek, és tanúsítvánnyal védettek. |
| OpenIddict tokenek/engedélyezések (authorization)/alkalmazás-konfiguráció | A meglévő keretrendszer-kezelés plusz tárolási titkosítás. A protokoll aláírási/titkosítási viselkedése változatlan. |
| OIDC aláírókulcs, kliens titok, adatbázis jelszó és CrowdSec kulcs a telepítési konfigurációban | Az adatbázison kívül; védd a telepítési környezetet és annak mentéseit host jogosultságokkal/tárolási titkosítással. Soha ne commitold az éles értékeket. |
| Felhő hitelesítő adatok, külső pénzügyi integrációs tokenek, backup jelszavak, külön cél (goal) entitás | Ebben a verzióban nem azonosítható megfelelő megvalósított tárolás. Osztályozd és védd őket a bevezetésükkor. |
| Pénzügyi cache payloadok | Alkalmazásszintű titkosítás felhasználó/művelet-specifikus céllal. A Valkey-n továbbra is ki van kapcsolva a perzisztencia. A cache-kulcs dimenziói hash-eltek; a metaadat látható marad. A régi cache bejegyzések a korábbi névtér alatt járnak le. |

A host swap, a core dumpok, a konténer logok, a reverse-proxy logok és a szolgáltatói pillanatképek külön, tartós felületek. A teljes tárolási garanciához tartsd kikapcsolva vagy titkosítva a swapot, kerüld az alkalmazás core dumpjait, és alkalmazz megfelelő titkosítást/megőrzési szabályt a logokra és pillanatképekre. A mellékelt Compose override csak a három PostgreSQL adatkönyvtárat helyezi át; nem konfigurálja a teljes VPS fájlrendszert.

## Alkalmazás-tervezés

A `PocketLedger.Security` megosztja a keretrendszer kulcskonfigurációját és a mezőkonvertereket az Infrastructure, a Web és az Identity között, anélkül, hogy a hostokat függővé tenné a pénzügyi perzisztenciától. Az EF konverterek védik az írásokat, és visszafejtik a materializált mezőket/projekciókat. A cross-tenant háttérfolyamat (worker) ugyanazt a providert kapja, mint az API. Az EF modell-cache bejegyzések provider-példányonként el vannak választva. A regisztrált titkosítási provider nélküli, meglévő önálló kontextusok elérhetők maradnak az eszközök (tooling) számára; a telepített hostok mindig regisztrálják és megkövetelik a titkosítási konfigurációt.

A védett payload a szabványos Data Protection formátum, stabil alkalmazás-diszkriminátorral és `PocketLedger.Database.v1` plusz mezőnkénti céllal (purpose). Ne nevezd át ezeket az azonosítókat telepítések áthelyezésekor. A Data Protection nem egy kulcsrahúzható (turnkey) archiválási rendszer: hosszú élettartamú adatbázis-értékekhez való használata megköveteli **az összes** történelmi kulcskarika-fájl és a becsomagoláshoz (wrapping) használt tanúsítványuk megőrzését, ameddig bármely adatbázisnak/mentésnek szüksége van rájuk. Az automatikus lejárat nem jelenti azt, hogy egy kulcs törölhető.

A jegyzet-keresés (notes search) először SQL-ben alkalmazza a tulajdonos/dátum/számla/kategória/típus/összeg szűrőket, csak a jelölt azonosítókat/jegyzeteket streameli, visszafejti és `OrdinalIgnoreCase` móddal összehasonlítja, majd az egyező azonosítókat alkalmazza a lapozásra, számlálásra, napi összesítésekre és exportokra. A `%`, `_` és a backslash literál karakterek. Ez megőrzi a kis- és nagybetű-érzéketlen részkeresést, de a Unicode kis/nagybetű-egyezés eltérhet egy PostgreSQL locale-specifikus `ILIKE`-jától. Egy széles körű keresés O(jelölt jegyzetek) marad, O(találatok) azonosítóval a memóriában; nincs plaintext keresőindex. A számla/kategória rendezés a materializálás után történik, ugyanazzal a .NET összehasonlítóval (comparer), amit a többi nézet is használ.

## 1. szakasz: mezőtitkosítás a meglévő VPS tárolón

Készíts elő egy **új telepítést/adatbázis-készletet**. A séma migrációk szándékosan elutasítják a feltöltött, régi (legacy) adatbázisokat; nem titkosítják a meglévő sorokat a helyükön. Ez a megegyezés szerinti export/újralétrehozás/visszaállítás munkafolyamatot követi. Soha ne futtasd a régi alkalmazást egy titkosított adatbázis ellen, és ne fokozd le (downgrade) a sémáját feltöltött táblák mellett.

1. Exportálj egy titkosított `.plbackup` fájlt a meglévő UI-n keresztül, a jelszavát pedig tárold külön. Ez nem tartalmazza az Identity felhasználókat, a TOTP-t, a helyreállítási kódokat és a Web munkameneteket. Ellenőrizd, hogy ez a várt adatkészlet, mielőtt bármit is leállítanál.
2. Készíts elő egy friss könyvtárat a repón kívül és az adatbázis-kötetektől (volume) is függetlenül:

   ```bash
   sudo bash tools/initialize-encryption-keys.sh /opt/pocketledger-security
   ```

   A script elutasítja a meglévő könyvtárakat. Létrehoz külön `api`, `web`, `identity` kulcs/tanúsítvány könyvtárakat és jelszó nélküli `active.pfx` fájlokat, amelyeket host jogosultságok védenek. A root-ként történő futtatás a .NET image 1654-es UID-jét rendeli hozzájuk. Ha egyéni konténer UID-t használsz, állítsd be ennek megfelelően a tulajdonjogot. Maga a PFX érzékeny kulcsanyag; az üres PFX jelszó szándékos, mivel a felügyelet nélküli konténer-újraindítások fájlrendszer-hozzáférés-vezérlést használnak. A LUKS később offline védi ezt a fájlt.
3. Állítsd be a `POCKETLEDGER_SECURITY_DIRECTORY=/opt/pocketledger-security` értéket a telepítési `.env` fájlban. A Compose csak az egyes hostok saját könyvtárait csatolja. A mount-forrásoknak léteznie kell; a Compose nem hozza létre őket automatikusan.
4. Állítsd le a régi telepítést a meglévő Compose projektjében. Indítsd el az új verziót egy **eltérő Compose projektnévvel**, például `pocketledger-encrypted` néven, hogy a három elnevezett PostgreSQL kötet üresen induljon. Ne futtasd a két telepítést egyszerre ugyanazokon a nyilvános portokon.

   ```bash
   docker compose -p pocketledger-encrypted build
   docker compose -p pocketledger-encrypted up -d identity-database
   docker compose -p pocketledger-encrypted run --rm identity bootstrap-identity
   docker compose -p pocketledger-encrypted up -d
   ```

5. Hozd létre az új Identity/TOTP beállítást, mentsd el az új helyreállítási kódokat, és állítsd vissza a `.plbackup` fájlt a UI-n keresztül. Hasonlítsd össze a tranzakciók számát, a számlákat, egyenlegeket, a tervező (planner) előzményeit és a jegyzeteket a régi exporttal. Az importált mezők automatikusan titkosítva lesznek a mentéskor.
6. Készíts biztonsági mentést az új kulcskarikákról **az első használat után**, valamint a privát tanúsítványaik külön, biztonságosan tárolt másolatáról. A pénzügyi `.plbackup` nem kulcskarika-mentés. Csak addig tartsd meg a régi telepítést, amíg a helyreállítást nem igazoltad, majd szándékosan vond ki forgalomból annak plaintext köteteit és a szolgáltatói pillanatképeket. Egy Docker kötet törlése nem garantálja a biztonságos törlést SSD-ken vagy a szolgáltatói mentésekben.

A migrációs védelem a régi, feltöltött Web adatbázist is elutasítja, ahelyett hogy csendben törölné a régi munkamenet-kulcskarikát. Egy új Web adatbázis is része ennek a resetnek. A visszaállás (rollback) a régi alkalmazást használja a régi adatbázisával, vagy egy friss, régi verziójú adatbázist, amelyet a pénzügyi exportból állítottak vissza; soha ne irányítsd az új, titkosított adatbázisra.

## 2. szakasz: manuálisan feloldott LUKS tárolás Ubuntu 22.04 LTS-en

Ez a szakasz egy azonosított, dedikált blokkeszközt vagy egy önállóan megtervezett tárolási migrációt igényel. **Ne futtass formázó parancsot egy meglévő root/adat partíció ellen.** A repó nem választ vagy formáz eszközt automatikusan.

Hozz létre egy LUKS2 eszközt a `cryptsetup`-pal, tartsd a jelszót (passphrase) a VPS lemezen kívül, és mentsd el külön a LUKS fejlécet. Oldd fel `/dev/mapper/pocketledger-data` néven, hozz létre egy ext4 fájlrendszert az új leképezésen (mapping), és csatold `/srv/pocketledger` alá. Ezeket az eszközspecifikus előkészítési lépéseket a VPS elrendezéséhez kell igazítani. Nincs automatikus feloldási belépési pont vagy jelszó-kulcsfájl mellékelve.

A csatolás után hozd létre a `/srv/pocketledger/databases/{api,web,identity}` és a `/srv/pocketledger/security` könyvtárakat. Ha az 1. szakasz már tartalmaz adatot, állíts le minden alkalmazás- és adatbázis-folyamatot, mielőtt bármit is átmozgatnál; másold át a teljes PostgreSQL könyvtárakat leállított állapotban, megőrizve a tulajdonjogot/jogosultságokat, és másold át a **meglévő** biztonsági (security) könyvtárat. Ne generálj helyettesítő kulcsokat. A PostgreSQL-nek ugyanazon a főverzión kell maradnia, és az eredeti adatkönyvtáraknak elérhetőnek kell maradniük visszaállás céljából, amíg a validáció sikeres nem lesz. Alternatívaként inicializálhatsz friss adatbázisokat az új csatoláson, és visszaállíthatsz egy titkosított pénzügyi mentést, újra létrehozva az Identityt.

Használd a wrappert minden titkosított tárolási Compose művelethez, megőrizve a választott projektnevet:

```bash
sudo bash tools/compose-encrypted-storage.sh -p pocketledger-encrypted config --quiet
sudo bash tools/compose-encrypted-storage.sh -p pocketledger-encrypted up -d
```

A wrapper ellenőrzi a csatolást, annak mapper eszközét és a LUKS2 állapotát, ráerőlteti a biztonsági könyvtárat erre a csatolásra, majd a `compose.encrypted-storage.yaml`-t használja. Az override mindhárom adatbázis-csatolást bind mountokra cseréli a LUKS-on, és kikapcsolja a Docker újraindítási szabályzatokat (restart policy) a három alkalmazáshoz és adatbázishoz. Ez szándékosan manuális indítást igényel újraindítás után. Ne használd önmagában az alap Compose fájlt, miután áttértél erre a szakaszra, és ne kerüld meg a wrappert. Egy egyéni Docker/systemd automatikus indításnak ugyanazokat a csatolás-ellenőrzéseket kell követnie.

Egy VPS újraindítás után manuálisan oldd fel az eszközt, csatold a `/srv/pocketledger`-t, majd futtasd a wrapper `up -d` parancsát. A leképezés lezárása előtt futtasd a wrapper `down` parancsát, csatold le a fájlrendszert, majd zárd le a leképezést. Egy egyszerű alkalmazás-konténer újraindításhoz, amíg a fájlrendszer csatolva marad, nincs szükség új jelszóra.

A zárolt blokkeszköz nyers offline másolata titkosított. Egy `pg_dump`, nyílt szöveges alkalmazás-export, tar másolat a csatolt könyvtárból, vagy egy plaintext állapotból az átállás előtt készített pillanatkép **nem** válik titkosítottá ettől a változtatástól.

## Kulcsrotáció és helyreállítás

- A keretrendszer adatkulcsai automatikusan rotálódnak (alapértelmezett élettartam: 90 nap). Tartsd meg a lejárt kulcsokat korlátlan ideig, amíg adat vagy mentés hivatkozik rájuk. Soha ne töröld vagy vond vissza a régi kulcsokat rutinszerű rotációként; a régi adatbázis-értékek nem íródnak újra automatikusan.
- A becsomagoló (wrapping) tanúsítványok rotálásához generálj új RSA tanúsítványt az inicializáló segédprogrammal egy új staging könyvtárban, tartsd meg az előző PFX-et, majd telepítsd az új `active.pfx`-et. Konfiguráld a `Encryption__PreviousCertificatePaths__0=/run/pocketledger-certificates/previous.pfx` értéket (és további indexeket) az érintett hoston. Tartsd meg minden régi, becsomagoló privát kulcsot, amelyre a meglévő kulcskarika XML fájloknak szükségük van. A rotáció az újonnan generált keretrendszer-kulcsokra hat; nem csomagolja újra a történelmi XML-t, és nem titkosítja újra azonnal a sorokat.
- Kompromittálódás utáni helyreállításhoz állítsd le a hozzáférést, állítsd vissza/titkosítsd újra egy külön megtervezett karbantartási eljáráson keresztül, és rotáld az érintett protokoll hitelesítő adatokat szükség szerint. A rutinszerű tanúsítványcsere önmagában nem orvosolja az ellopott adatkulcsokat. Egy friss, titkosított `.plbackup` fájlból új kulcskarikák alatt visszaállított adatbázis újra titkosíthatja a pénzügyi adatokat; az Identitynek továbbra is saját helyreállítási terv szükséges.
- Egy adatbázis-mentés helyreállításához állítsd vissza a helyes host kulcskarika-könyvtárakat, minden szükséges tanúsítvány privát kulcsot, a jogosultságokat, az alkalmazásneveket és az adatbázist együtt. Igazold vissza a helyreállítást egy elszigetelt telepítésben, mielőtt kivonnád az eredetieket. A kulcsmentéseket tárold külön az adatbázis-mentésektől, és védd offline titokkal.
- A hiányzó konfigurációnak, hiányzó tanúsítványoknak, elérhetetlen kulcsoknak vagy érvénytelen titkosított szövegnek hibát kell okoznia; ne cseréld le a titkosított értékeket üres szövegre. Egy újonnan generált kulcskarika nem tudja visszafejteni a régi sorokat. Állítsd vissza inkább a történelmi kulcskarikát.
- A fejlesztés egy állandó, host-onkénti `.local/encryption` könyvtárat használ, becsomagoló tanúsítvány nélkül. Ezeket a könyvtárakat a Git figyelmen kívül hagyja, és ki vannak zárva a Docker build kontextusból. Nem alkalmasak éles kulcstárolásra. Az elvesztésük az adott fejlesztési adatbázis titkosított mezőihez való hozzáférés elvesztését is jelenti.

## Validáció és teljesítmény

Futtasd a meglévő build/teszt csomagot és a szintetikus kriptográfiai benchmarkot:

```bash
dotnet build PocketLedger.slnx -m:1
dotnet test PocketLedger.slnx --no-build -m:1
dotnet run --project tools/PocketLedger.EncryptionBenchmark -c Release
```

Egy helyi .NET 10.0.12 Linux mérés (10 000 művelet, bemelegített provider, 20 jelentett logikai CPU) az alábbi lefolyt időket produkálta. Ezek jelzésértékű, egyszeri futtatásból származó számok, nem telepítési SLA-k:

| Szintetikus szöveg | Protect | Unprotect | Visszafejtés + részkeresés | Tárolt karakterek |
| --- | ---: | ---: | ---: | ---: |
| 100 ékezetes karakter (200 UTF-8 byte) | 210 ms | 184 ms | 227 ms | 390 |
| 500 ékezetes karakter (1 000 UTF-8 byte) | 323 ms | 383 ms | 409 ms | 1 456 |
| 10 000 ékezetes karakter (20 000 UTF-8 byte) | 1 731 ms | 1 288 ms | 906 ms | 26 800 |

A benchmark jelenti a titkosítás/visszafejtés/keresés CPU-idejét, az allokációkat és a titkosított szöveg mérethízását (ciphertext expansion). Csak szintetikus adatot és egy eldobható tanúsítványt/kulcskarikát használ; nem kapcsolódik alkalmazás-adatbázishoz. Az adatbázis I/O, az EF materializáció, a széles körű keresés valós adatkészleten mért költsége és a LUKS overhead telepítési méréseket igényel. A PostgreSQL/Valkey integrációs csomag a meglévő tesztvégpontjait igényli; a hagyományos memórián belüli tesztek nem bizonyítják az adatbázis titkosított szövegét vagy a LUKS védelmét.

A megvalósítás validációja egy elszigetelt, helyi PostgreSQL 18 példányt használt (a mellékelt éles image továbbra is PostgreSQL 17). Mindhárom séma sikeresen migrálódott. A manuális API írások/visszaolvasások megerősítették a titkosított számlaneveket, tranzakció-jegyzeteket és tervező-pillanatképeket; az ékezetes és literál-wildcard keresések, összesítés/egyenleg, az akkor elérhető backup-szerializációs és visszaállítási folyamat, valamint az API-újraindítás utáni visszaolvasás sikeres volt. Az Identity bootstrap és a hitelesítő-kulcs visszaállítása titkosított `AspNetUserTokens.Value`-t eredményezett. A kulcskarika XML tanúsítvánnyal titkosított titkokat tartalmazott. A LUKS wrapper elutasította a futtatást a csatolása nélkül. Ez nem mérése vagy ellenőrzése a valódi VPS-nek vagy a PostgreSQL 17 konténernek.

A régi telepítés kivonása előtt ellenőrizd az engedélyezett olvasásokat újraindítás után, a titkosított backup visszaállítását, a jegyzet-keresést (beleértve az ékezeteket és a literál `%`/`_`-t), a számlálásokat és lapozást, a napi összesítéseket, egyenlegeket, ismétlődő tranzakciókat, a tervező Notes-ait/előzményeit, a TOTP-t/helyreállítást, valamint egy teljes kulcs/adatbázis visszaállítást. Vizsgáld meg a nyers PostgreSQL oszlopokat az elszigetelt telepítésben, hogy megerősítsd a titkosított szöveget, és erősítsd meg, hogy az indítás elutasítja a nem csatolt LUKS fájlrendszert.

## Hivatkozások

- [PostgreSQL 17 titkosítási opciók](https://www.postgresql.org/docs/17/encryption-options.html)
- [ASP.NET Core Data Protection konfiguráció](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [Data Protection kulcskezelés és megőrzés](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0)
