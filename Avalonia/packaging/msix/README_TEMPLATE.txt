GitRunnerManager Windows MSIX telepítés
Verzió: <verzio>
Platform: <runtime>

A ZIP tartalma:
- <msix>
- GitRunnerManager-Sideload.cer
- readme.txt

Telepítés:
1. Csomagold ki ezt a ZIP fájlt egy tetszőleges mappába.
2. Nyisd meg a GitRunnerManager-Sideload.cer fájlt.
3. Válaszd a Tanúsítvány telepítése lehetőséget.
4. Válaszd a Helyi gép tárolót, majd a Tanúsítványok elhelyezése a következő tárolóban opciót.
5. Tallózd be a Megbízható legfelső szintű hitelesítésszolgáltatók tárolót, majd fejezd be az importálást.
6. Indítsd el a <msix> fájlt, és telepítsd az alkalmazást.

Megjegyzés:
- A tanúsítvány self-signed, ezért a .cer telepítése szükséges az .msix telepítése előtt.
- A .cer fájlt ugyanebből a ZIP-ből használd, mint az .msix csomagot.

GitRunnerManager Windows MSIX installation
Version: <verzio>
Platform: <runtime>

ZIP contents:
- <msix>
- GitRunnerManager-Sideload.cer
- readme.txt

Installation:
1. Extract this ZIP file to any folder.
2. Open GitRunnerManager-Sideload.cer.
3. Choose Install Certificate.
4. Choose Local Machine, then select Place all certificates in the following store.
5. Browse to Trusted Root Certification Authorities, then finish the import.
6. Open <msix> and install the application.

Note:
- The certificate is self-signed, so the .cer file must be installed before the .msix package.
- Use the .cer file from the same ZIP as the .msix package.
