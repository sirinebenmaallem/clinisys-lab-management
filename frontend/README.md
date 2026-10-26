# Interface Clinisys

Interface française HTML/CSS/JavaScript et jQuery 3.7.1 local (licence MIT conservée dans le fichier fournisseur). Aucun outil de build frontend, CDN ou serveur distinct n’est nécessaire.

## Lancement

Depuis la racine, avec .NET 10 et le certificat HTTPS de développement approuvé :

```powershell
dotnet run --project src/Clinisys.API --configuration Release --launch-profile https
```

Ouvrir **https://localhost:7158/**. Ne pas ouvrir `index.html` directement et ne pas lancer un serveur frontend séparé. Les fichiers sont copiés dans `wwwroot` à la compilation et à la publication de l’API. Après une modification frontend, reconstruire l’API.

La base et un compte doivent déjà avoir été préparés par l’opérateur suivant la [documentation d’authentification](../README.md#authentication-and-authorization-step-07). Le lancement de l’interface n’applique aucune migration et ne crée aucun compte. Ne pas placer de connexion SQL ni de mot de passe dans ce dossier.

## Écrans et autorisations

- Tous les rôles : catalogue en lecture, recherche de patients sans CIN/téléphone dans les listes, demandes et PDF après validation.
- Secretary : création/correction/consultation détaillée des patients, création de demandes et délivrance.
- Technician : saisie/correction des résultats, démarrage et clôture de saisie.
- Biologist : gestion du catalogue, résultats, workflow, validation officielle et délivrance.

Les rôles multiples cumulent leurs possibilités. L’API reste l’autorité : masquer une action dans l’interface ne remplace jamais sa policy serveur.

Les demandes utilisent les analyses et bilans réellement renvoyés par le catalogue ; les prix, remises, dédoublonnages et numéros sont calculés par le backend. Les références affichées proviennent des snapshots des résultats. Le LDL calculé est indiqué sans permettre sa correction manuelle. Aucun résultat fictif n’est fourni.

## Contrats utilisés

- `/api/auth/antiforgery`, `/login`, `/session`, `/logout` : session par cookie sécurisé, token CSRF en mémoire, renouvelé après connexion.
- `/api/medical-analyses`, `/api/analysis-panels` et leurs routes existantes de désactivation, références et associations.
- `/api/patients` : recherche paginée, création, détail et PUT `{ patient, version }`.
- `/api/laboratory-requests` : liste paginée, création et détail ; transitions `start`, `complete-entry`, `validate`, `deliver` avec `{ version }`.
- Routes `/{requestId}/items/{itemId}/result` et `/{requestId}/results` : saisie, correction avec version et consultation.
- `/{requestId}/report` : téléchargement authentifié PDF.

Les recherches catalogue/patients et listes de demandes sont paginées. Les dates des demandes sont affichées en UTC ; le filtre de fin est exclusif. Une erreur 409 demande une actualisation explicite, sans replay automatique. Après une écriture au résultat incertain (réseau ou `outcomeUnknown`), vérifier les données avant toute nouvelle tentative.

## Sécurité et limites

Aucun mot de passe, cookie ou donnée patient n’est enregistré dans localStorage/sessionStorage. Le contenu serveur est inséré comme texte. Les API conservent `Cache-Control: no-store`. Les actions médicales ne sont exécutées qu’après connexion ; le shell de connexion est public. Les cookies HttpOnly restent gérés par le navigateur.

L’administration des comptes reste une commande locale, sans nouvel endpoint. Aucun écran d’audit ni d’administration HTTP n’est inventé. Le produit conserve les limites de déploiement et de sécurité du backend ; cette interface ne constitue pas une validation de production.

La vérification complète navigateur avec des utilisateurs réels et une base préparée reste à effectuer dans l’environnement autorisé. Aucun accès SQL n’est nécessaire aux tests d’hébergement et d’autorisation HTTP en mémoire.
