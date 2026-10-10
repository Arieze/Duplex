# Développement

## Prérequis

- Windows 10 ou 11 avec .NET Framework 4.8.
- Windows PowerShell 5.1 ou PowerShell 7.
- Le compilateur `csc.exe` fourni par .NET Framework.

## Structure

```text
src/
  App.cs                 Fenêtre, commandes et orchestration asynchrone
  Engine.cs              Inventaire, comparaison, copies et sauvegardes
  MainWindow.xaml        Interface WPF
  app.manifest           Compatibilité Windows et DPI
  icon.ico               Icône embarquée
tests/
  EngineTests.cs         Scénarios de fichiers dans des dossiers isolés
  UiTests.cs             Chargement WPF, sélection, filtres et modes
scripts/
  build.ps1              Compilation vers dist/Duplex.exe
  test.ps1               Exécution des deux suites de tests
  package.ps1            Compilation, tests, ZIP et SHA256SUMS.txt
docs/
  images/interface.png   Aperçu avec données fictives
dist/                    Sorties locales, non versionnées
test-results/            Rapports et fixtures locales, non versionnés
```

## Vérification locale

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

Les scénarios créent leurs propres fichiers sous `test-results/`. Ils ne consultent pas les préférences et ne synchronisent aucun dossier personnel. Les fixtures restent disponibles pour inspection. `resultats.txt` contient le bilan du moteur, `interface.txt` le bilan WPF.

Les tests WPF chargent le vrai XAML et déclenchent les commandes de sélection, de recherche, de changement de mode et de permutation, sans ouvrir de fenêtre. Ils ne remplacent pas un essai manuel des dialogues natifs et du glisser-déposer.

## Architecture et garanties

L’inventaire utilise des chemins relatifs. Lorsqu’un volume renvoie plusieurs graphies d’un même chemin, l’identité Windows du fichier est vérifiée avant de supprimer le second passage de l’inventaire. Deux objets distincts qui ne diffèrent que par la casse provoquent une erreur explicite.

Le moteur compare les fichiers communs puis propose les opérations. Une erreur d’analyse bloque la synchronisation. Avant chaque copie, il vérifie les informations mémorisées, écrit un fichier temporaire, calcule son SHA-256 et conserve l’ancienne destination dans `.duplex-backups`. Un échec de mise en place déclenche une tentative de restauration.

Les copies et calculs de hash s’exécutent hors du thread WPF. L’annulation est coopérative : les opérations déjà terminées sont conservées. Les tests sont embarqués pour pouvoir vérifier l’exécutable distribué avec `--self-test` et `--ui-test`.

## Préparer une version

1. Mettre à jour la version dans `src/App.cs` et `CHANGELOG.md`.
2. Lancer `scripts/package.ps1` et consulter les deux rapports.
3. Vérifier l’aperçu de l’interface et les opérations sur des dossiers jetables.
4. Publier une release avec `Duplex.exe`, le ZIP portable et `SHA256SUMS.txt`.

L’exécutable est actuellement non signé. Les empreintes contrôlent l’intégrité des téléchargements, pas l’identité de l’éditeur.
