# Contribuer

Décrivez le comportement attendu et fournissez un cas reproductible avec des fichiers fictifs. Ne joignez ni identifiants, ni chemins personnels, ni documents privés.

Pour une modification :

1. Créez une branche dédiée.
2. Limitez le changement au problème traité.
3. Ajoutez un scénario de non-régression si le moteur change.
4. Lancez `scripts/build.ps1` puis `scripts/test.ps1` sous Windows.
5. Indiquez les vérifications effectuées dans la pull request.

Toute modification du miroir, des exclusions ou des sauvegardes doit préserver le refus des analyses incomplètes et la récupération des versions remplacées.
