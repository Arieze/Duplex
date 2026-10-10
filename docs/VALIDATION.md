# Validation — Duplex 1.1.0

Vérification locale du 10 octobre 2026, sous Windows et .NET Framework 4.8.

| Vérification | Résultat |
|---|---|
| Compilation de l’exécutable distribué | Réussie |
| Moteur de comparaison et synchronisation | 31 contrôles réussis |
| Chargement WPF et commandes de l’interface | 9 contrôles réussis |
| Rendu de l’interface avec des données fictives | Vérifié visuellement |

## Couverture du moteur

Comparaison SHA-256 de contenus différents de même taille/date, fichiers identiques avec des dates différentes, copie dans les deux sens, fusion avec conflits ignorés, miroir avec archivage, sauvegarde des versions remplacées, chemins Unicode, fichiers modifiés depuis l’analyse, annulation, dossiers imbriqués, sortie du dossier cible, collision fichier/dossier, destination apparue entre analyse et copie, exclusions des fichiers cachés, fichier verrouillé, refus des analyses incomplètes, nettoyage des fichiers temporaires, entrées répétées et alias de casse.

## Couverture de l’interface

Chargement du XAML et des données, synchronisation désactivée avant comparaison, tout décocher, tout cocher sans résoudre les conflits, recherche, changement de mode, permutation des chemins, invalidation de l’analyse et retour à l’état vide.

## Portée

Les tests utilisent des fixtures isolées. Ils n’effectuent aucune synchronisation sur les dossiers personnels. Les résultats ne constituent pas une validation exhaustive de tous les systèmes de fichiers, disques réseau ou scénarios de coupure électrique. Les dialogues Windows et le glisser-déposer ne sont pas automatisés par cette suite.

Pour reproduire : `scripts/build.ps1` puis `scripts/test.ps1`. Les rapports et fixtures sont générés sous `test-results/` et exclus du dépôt.
