# Duplex

Application Windows de comparaison et de synchronisation de dossiers, en français.

## Démarrer

Double-cliquez sur **Duplex.exe**. Aucun compte et aucune installation de l’application ne sont nécessaires. L’exécutable contient son interface et son icône ; vous pouvez le déplacer seul dans le dossier de votre choix.

Configuration : Windows 10 ou Windows 11 avec **.NET Framework 4.8**. L’application a été compilée et testée sur ce poste. L’exécutable n’est pas signé avec un certificat d’éditeur.

## Utilisation

1. Choisissez les dossiers **A** et **B**, saisissez leurs chemins ou déposez-les dans les champs.
2. Choisissez le mode puis cliquez sur **Comparer**. Cette étape ne modifie aucun fichier des dossiers.
3. Examinez les différences. Recherchez un chemin, cochez les fichiers à traiter et changez leur action si nécessaire.
4. Cliquez sur **Synchroniser**. Un récapitulatif indique les copies, remplacements et archivages avant confirmation.
5. Relancez la comparaison après une synchronisation pour connaître le nouvel état.

| Mode | Comportement |
|---|---|
| A → B | Ajoute dans B les fichiers manquants et met à jour les versions différentes depuis A. Garde les fichiers propres à B. |
| B → A | Même fonctionnement, en sens inverse. |
| Fusion | Copie les fichiers propres à chaque côté vers l’autre. Les conflits sont ignorés tant que vous n’avez pas choisi une version. |
| Miroir de A | Met B en accord avec A pour les fichiers sélectionnés. Les fichiers propres à B sont déplacés vers les sauvegardes. |

La comparaison porte sur les **chemins relatifs et le contenu des fichiers**. Elle ne recherche pas les doublons sous des noms différents. Les sous-dossiers sont parcourus ; les dossiers vides ne sont pas synchronisés.

L’option **Comparer le contenu** utilise SHA-256 et est activée par défaut. Deux fichiers de même taille et de même date peuvent donc être identifiés comme différents. Elle nécessite la lecture complète des fichiers communs de même taille : prévoyez davantage de temps pour un dossier volumineux ou un disque lent.

Le mode rapide, accessible en décochant cette option, compare uniquement taille et date. L’interface indique alors « présumés identiques ».

## Sauvegardes et récupération

Les versions remplacées et les fichiers retirés par le miroir sont conservés dans le dossier concerné :

```
.duplex-backups
└── 2026-10-09_14-30-00_abcdef
    ├── journal.txt
    └── fichiers
        └── chemin relatif du fichier conservé
```

Le bouton **Sauvegardes** ouvre les sessions de la dernière synchronisation, ou les dossiers de sauvegarde existants. Pour récupérer une version, copiez le fichier depuis `fichiers` vers le même chemin dans le dossier d’origine. Le journal indique les opérations.

Les sauvegardes sont exclues des comparaisons et ne sont jamais purgées automatiquement. Elles occupent de l’espace sur le disque qui reçoit les changements. Une sauvegarde sur le même disque ne protège pas d’une panne de ce disque.

## Fonctionnement et limites

- Chaque copie est écrite dans un fichier temporaire, puis vérifiée par SHA-256 avant sa mise en place.
- L’ancien fichier est sauvegardé avant un remplacement. Un échec de mise en place provoque une tentative de restauration immédiate.
- Les fichiers modifiés depuis l’analyse, verrouillés ou inaccessibles sont refusés et signalés.
- Une analyse incomplète bloque la synchronisation, pour ne pas confondre des fichiers illisibles avec des fichiers absents.
- Les liens symboliques et jonctions ne sont pas suivis. Leur chemin est exclu des deux côtés. Il en va de même pour les fichiers cachés si vous choisissez de les exclure.
- Les dossiers identiques ou imbriqués sont refusés.
- Le bouton **Annuler** interrompt le travail aux points de contrôle. Les opérations déjà terminées restent effectuées.
- Les collisions entre un fichier et un dossier sont signalées, sans remplacement automatique du dossier.
- Les dates de modification sont conservées lors de la copie. Les droits NTFS, flux alternatifs et autres métadonnées du système de fichiers ne sont pas reproduits.
- Les chemins très longs dépendent également de la configuration Windows et du disque cible ; les erreurs sont affichées.
- Il s’agit d’une synchronisation **manuelle** : pas de surveillance permanente, de service en arrière-plan ni de planification.
- Les deux derniers chemins et le mode sont mémorisés dans `%LOCALAPPDATA%\Duplex\preferences.xml`. L’application n’envoie pas de données à un service externe.

## Sources et vérification

Les sources C# et XAML sont dans `src`. Le script `scripts/build.ps1` compile l’exécutable avec le compilateur .NET Framework Windows, sans téléchargement de dépendances.

Pour exécuter les contrôles du moteur dans un dossier de test isolé :

```
Duplex.exe --self-test C:\chemin\vers\tests-duplex
```

Le rapport est écrit dans `resultats.txt` et les jeux de test sont conservés pour inspection. Les contrôles couvrent les comparaisons, les deux sens de copie, les conflits de fusion, le miroir, les sauvegardes, l’annulation, les chemins Unicode, les changements depuis l’analyse, les exclusions et les collisions fichier/dossier.
