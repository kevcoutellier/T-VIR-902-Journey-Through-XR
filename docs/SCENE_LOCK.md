---
owner: FREE
since: 
feature: 
expected_release: 
---

# Verrou de la scène principale `LivingRoom.unity`

Ce fichier est le **seul mécanisme de coordination** pour la scène `STOP-IT/Assets/_Scenes/LivingRoom.unity`.  
Un hook Claude bloque automatiquement toute modification de la scène si `owner` n'est pas votre nom git.

---

## Comment réserver le verrou

1. Faire `git pull` pour vérifier l'état actuel
2. Si `owner: FREE`, éditer ce fichier :
   ```yaml
   owner: VotreNom          # doit correspondre à git config user.name
   since: YYYY-MM-DD HH:MM
   feature: description courte de ce que vous intégrez
   expected_release: YYYY-MM-DD HH:MM
   ```
3. Committer et pusher **immédiatement** :
   ```
   git add docs/SCENE_LOCK.md
   git commit -m "chore(lock): claim by <nom> for <feature>"
   git push
   ```
4. Prévenir l'équipe sur Discord

## Comment libérer le verrou

Dès que l'intégration est terminée et commitée :
1. Remettre ce fichier à l'état libre :
   ```yaml
   owner: FREE
   since: 
   feature: 
   expected_release: 
   ```
2. Committer et pusher :
   ```
   git add docs/SCENE_LOCK.md
   git commit -m "chore(lock): release"
   git push
   ```
3. Prévenir l'équipe sur Discord

## Timeout

Si le verrou est actif depuis plus de **4 heures** sans réponse Discord, il peut être libéré de force.  
Voir `docs/TEAM_WORKFLOW.md` → section "Situations d'urgence" pour la procédure complète.

## Historique

L'historique des réservations est visible via git :
```powershell
git log --oneline docs/SCENE_LOCK.md
git log -p docs/SCENE_LOCK.md
```
