# check-scene-lock.ps1
# Hook PreToolUse : bloque toute modification de LivingRoom.unity si le verrou n'appartient pas à l'utilisateur courant.
# Exit 0 = autorisé, Exit 2 = bloqué (Claude affiche le message stderr à l'utilisateur).

param()

# Chemins protégés (relatifs à la racine du repo)
$PROTECTED_PATHS = @(
    "STOP-IT/Assets/_Scenes/LivingRoom.unity",
    "STOP-IT\Assets\_Scenes\LivingRoom.unity",
    "LivingRoom.unity",
    "LivingRoom"
)
$LOCK_FILE = "docs/SCENE_LOCK.md"

# Bypass d'urgence
if ($env:HOOK_BYPASS -eq "1") {
    Write-Host "[VERROU] Bypass activé (HOOK_BYPASS=1). Attention : utiliser avec discernement." -ForegroundColor Yellow
    exit 0
}

# Lire l'input JSON depuis stdin
$inputJson = $null
try {
    $rawInput = $input | Out-String
    if ([string]::IsNullOrWhiteSpace($rawInput)) {
        exit 0
    }
    $inputJson = $rawInput | ConvertFrom-Json
} catch {
    # Si on ne peut pas parser l'input, on laisse passer (sécurité permissive en cas d'erreur de hook)
    exit 0
}

$toolName = $inputJson.tool_name
$toolInput = $inputJson.tool_input

# Extraire le chemin cible selon le type d'outil
$targetPath = $null

if ($toolInput.file_path) {
    $targetPath = $toolInput.file_path
} elseif ($toolInput.path) {
    $targetPath = $toolInput.path
} elseif ($toolInput.scene_name) {
    $targetPath = $toolInput.scene_name
} elseif ($toolInput.scene_path) {
    $targetPath = $toolInput.scene_path
}

if ($null -eq $targetPath) {
    exit 0
}

# Vérifier si le chemin cible est protégé
$isProtected = $false
foreach ($p in $PROTECTED_PATHS) {
    if ($targetPath -like "*$p*") {
        $isProtected = $true
        break
    }
}

if (-not $isProtected) {
    exit 0
}

# Lire le fichier de verrou
if (-not (Test-Path $LOCK_FILE)) {
    Write-Error "[VERROU] Fichier $LOCK_FILE introuvable. Créez-le avant de continuer (voir docs/TEAM_WORKFLOW.md)."
    exit 2
}

$lockContent = Get-Content $LOCK_FILE -Raw
$owner = $null
$since = $null
$feature = $null

foreach ($line in ($lockContent -split "`n")) {
    if ($line -match "^owner:\s*(.+)$") {
        $owner = $matches[1].Trim()
    } elseif ($line -match "^since:\s*(.+)$") {
        $since = $matches[1].Trim()
    } elseif ($line -match "^feature:\s*(.+)$") {
        $feature = $matches[1].Trim()
    }
}

if ($null -eq $owner -or $owner -eq "") {
    Write-Error "[VERROU] Impossible de lire le champ 'owner' dans $LOCK_FILE. Vérifiez le format du fichier."
    exit 2
}

# Si le verrou est libre, bloquer quand même et demander de le réserver explicitement
if ($owner -eq "FREE") {
    Write-Error @"
[VERROU] La scène LivingRoom.unity est actuellement LIBRE mais vous devez d'abord réserver le verrou.

Procédure :
  1. Éditez docs/SCENE_LOCK.md : remplissez owner, since, feature, expected_release
  2. git add docs/SCENE_LOCK.md
  3. git commit -m "chore(lock): claim by <votre-nom> for <feature>"
  4. git push

Voir docs/TEAM_WORKFLOW.md pour le workflow complet.
Bypass d'urgence : définir `$env:HOOK_BYPASS = "1"` avant de relancer.
"@
    exit 2
}

# Récupérer le nom git de l'utilisateur courant
$currentUser = $null
try {
    $currentUser = (git config user.name 2>$null).Trim()
} catch {
    $currentUser = ""
}

if ([string]::IsNullOrWhiteSpace($currentUser)) {
    Write-Error "[VERROU] Impossible de lire git config user.name. Configurez votre identité git : git config user.name 'VotreNom'"
    exit 2
}

# Comparer le propriétaire du verrou avec l'utilisateur courant
if ($owner -eq $currentUser) {
    # L'utilisateur courant possède le verrou — autoriser
    exit 0
}

# Verrou appartient à quelqu'un d'autre
$sinceInfo = if ($since) { "depuis $since" } else { "(heure non renseignée)" }
$featureInfo = if ($feature) { "pour : $feature" } else { "(feature non renseignée)" }

Write-Error @"
[VERROU] ⛔ Modification bloquée — LivingRoom.unity est verrouillée.

  Propriétaire actuel : $owner ($sinceInfo)
  Raison             : $featureInfo
  Vous êtes          : $currentUser

Actions possibles :
  → Attendez que $owner libère le verrou (il remettra owner: FREE et pushera)
  → Ou développez d'abord dans votre scène sandbox : STOP-IT/Assets/_Scenes/Sandboxes/
  → Bypass d'urgence : `$env:HOOK_BYPASS = "1"` (à documenter dans votre commit)

Vérifiez l'état actuel : cat docs/SCENE_LOCK.md
Historique : git log --oneline docs/SCENE_LOCK.md
"@
exit 2
