# Doodle Drive

Application Windows de bureau (WPF / .NET 10) façon « Google Drive personnel » :
fichiers stockés sur le disque dur de la Freebox exposé en **FTP**, comptes et
permissions dans une base **MariaDB** distante. Aucun backend intermédiaire :
l'app parle directement au FTP et à la base.

Interface Fluent (WPF-UI, effet Mica, thème clair/sombre auto), MVVM
(CommunityToolkit.Mvvm), FTP via FluentFTP, SQL via MySqlConnector + Dapper,
mots de passe en BCrypt.

---

## Prérequis

- **SDK .NET 10** (x64) : <https://dotnet.microsoft.com/download/dotnet/10.0>
  (`dotnet --version` doit renvoyer `10.x`).
- Le projet cible `net10.0-windows` et **compile proprement** (0 erreur, 0
  avertissement). Si tu préfères .NET 8, remplace `net10.0-windows` par
  `net8.0-windows` dans `Doodle Drive.csproj` (WPF-UI 4.3 supporte les deux).

> Note : au tout début le SDK `10.0.201` de cette machine était corrompu (dossier
> vide) ; il a depuis été remplacé par `10.0.301`, qui build sans souci.

---

## Configuration de la base et de l'admin

1. Crée la base et les tables avec ton script `cloud_perso` (schéma fourni).
2. Amorce le compte admin :
   ```sql
   SOURCE db/seed_admin.sql;
   ```
   Compte par défaut : **admin / admin** → à changer immédiatement via le panneau
   *Administration → Utilisateurs → Réinitialiser le mot de passe*.

Les identifiants MariaDB **et** FTP se saisissent au premier lancement, dans le
panneau *« Paramètres serveur »* de l'écran de connexion (ou plus tard dans
*Paramètres*). Ils sont enregistrés dans `%AppData%\DoodleDrive\config.json`.

---

## Compiler et lancer

```powershell
# Restaurer + lancer en debug
dotnet run --project "Doodle Drive.csproj"

# Ou ouvrir "Doodle Drive.slnx" dans Visual Studio 2022+ et F5
```

### Publier un exécutable autonome (single-file)

```powershell
dotnet publish "Doodle Drive.csproj" -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Le `.exe` se trouve dans `bin/Release/net10.0-windows/win-x64/publish/`.

---

## Architecture

```
Models/            Entités et enums (User, Folder, FolderNode, RemoteEntry, AppConfig…)
Services/          Accès données & logique transverse
  ├─ DatabaseService   MariaDB (MySqlConnector + Dapper) : users, folders, permissions
  ├─ FtpService        FTP (FluentFTP) : listing, upload/download avec reprise, CRUD dossiers
  ├─ AuthService       Vérification BCrypt
  ├─ ThumbnailService  Glyphes par type + vignettes images (cache disque)
  ├─ NotificationService  Toasts in-app non bloquants
  ├─ DialogService     Confirmations, saisies, sélecteurs fichiers/dossiers
  ├─ AppConfigService  Persistance config locale (JSON)
  └─ AppServices       Composition manuelle (sans framework DI)
ViewModels/        Un VM par écran (Login, Shell, Files, Admin, Settings, Permissions…)
Views/             Fenêtres et UserControls XAML (+ Dialogs/)
Converters/        Convertisseurs de binding
Helpers/           PasswordHelper (binding MVVM d'un PasswordBox)
db/                Script d'amorçage admin
```

### Règles d'accès implémentées
- **admin** : voit et gère tout (comptes + tous les dossiers), ignore `folder_permissions`.
  Peut faire **clic droit sur n'importe quel dossier → « Gérer les accès »** : le
  dossier est enregistré à la volée dans `folders` et attribué à un utilisateur.
  C'est l'admin qui décide quels dossiers un user voit (et donc son point d'entrée).
- **user** : ne voit que les dossiers dont il est `owner_id` ou présents dans
  `folder_permissions`. Il **atterrit directement** dans son 1er dossier attribué.
- **Modèle « couloir »** : si un user a accès à `/patate/eco+/lolilpop/cedossier`,
  il peut **remonter le chemin** (via le fil d'Ariane), mais dans chaque dossier
  intermédiaire (`patate`, `eco+`, `lolilpop`) il ne voit **que le sous-dossier
  qui mène** à son dossier autorisé — ni les autres dossiers/fichiers, aucune
  action possible (niveau *Traverse*). Dans `cedossier` et ses descendants, accès
  complet selon sa permission.
- Seul le **propriétaire** (ou un admin) peut gérer les partages d'un dossier.
- `read` = lecture/téléchargement ; `write` = + upload/suppression/renommage ;
  `owner` = tout, y compris le partage.

### Sécurité des réglages serveur
- Sur l'écran de connexion, les **paramètres serveur (MariaDB/FTP) sont
  verrouillés** : il faut saisir un identifiant/mot de passe **admin** puis
  « Déverrouiller ». Au tout premier lancement (base non encore configurée),
  l'accès est libre le temps de l'installation.
- Une fois connecté, un **user normal ne voit pas** les réglages serveur dans
  *Paramètres* (seulement l'apparence). Il ne peut donc pas changer son point
  d'entrée : c'est l'admin qui décide.

---

## Fonctionnalités

- **Connexion** BCrypt + mémorisation optionnelle de l'identifiant.
- **Navigation** : arbre latéral des dossiers accessibles, fil d'Ariane,
  vues **grille/liste**, recherche et tri (nom, date, taille, type).
- **Upload** : glisser-déposer de fichiers **et dossiers** depuis l'explorateur,
  sélecteur classique, file d'attente avec progression par fichier et **reprise**.
- **Download** : multi-sélection vers un dossier local, dossiers récursifs.
- **Aperçu** rapide des images intégré (double-clic) ; autres types ouverts avec
  l'application par défaut de Windows.
- **Dossiers** : créer / renommer / supprimer (FTP + miroir en base).
- **Partage** : panneau « Gérer les accès » (ajout/retrait, bascule read/write).
- **Admin** : CRUD comptes (rôle, reset mot de passe, suppression) + gestion de
  tous les dossiers.
- **UX** : thème clair/sombre suivant Windows, toasts non bloquants, Mica,
  panneaux redimensionnables, timeouts réseau (15–30 s) avec messages clairs.

---

## Notes & limites (choix de simplicité)

- **Vignettes** : les images ont une vraie miniature (téléchargée puis mise en
  cache dans `%Temp%\DoodleDrive\thumbs`). Vidéos/PDF affichent une **glyphe
  typée** (pas d'extraction ffmpeg/pdfium pour rester léger et fluide — la
  couche `ThumbnailService` est prête à être étendue si tu veux les ajouter).
- **Sécurité** : app perso volontairement simple. La config locale (dont les
  mots de passe MariaDB/FTP) est un JSON en clair dans `%AppData%`. À ne pas
  déployer tel quel sur une machine partagée.
- **Icônes** : glyphes *Segoe Fluent Icons* (Windows 11) avec repli *Segoe MDL2
  Assets* (Windows 10). Certaines peuvent être ajustées à ton goût dans
  `ThumbnailService.GetGlyph` et les vues.
- **Testé côté compilation uniquement** : la solution build proprement, mais je
  n'ai pas pu tester le comportement à l'exécution (il faut une vraie base
  MariaDB et un FTP joignables). Si un écran se comporte mal une fois connecté,
  dis-moi lequel et je corrige.

---

## Paquets NuGet

| Paquet | Rôle |
|---|---|
| WPF-UI 4.3.0 | UI Fluent (FluentWindow, Mica, thèmes, contrôles) |
| CommunityToolkit.Mvvm 8.4.0 | MVVM (ObservableObject, RelayCommand) |
| FluentFTP 52.1.0 | Client FTP (reprise upload/download) |
| MySqlConnector 2.4.0 | Client MariaDB async |
| Dapper 2.1.66 | Mapping SQL → objets |
| BCrypt.Net-Next 4.0.3 | Hash/vérification des mots de passe |
