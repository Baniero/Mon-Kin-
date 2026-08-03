# Mon Kine

Application de gestion pour cabinet de kinésithérapie et de rééducation.

## Fonctionnalités

- Onglet Patients:
  - Données générales: age, sexe, téléphones, adresse, couverture (CNAM, civil payant, autre)
  - Fiche médicale: diagnostic, médecin traitant, nombre de séances, nature des séances (liste + nouvelle indication)
- Onglet Rendez-vous:
  - Sous-onglet planning hebdomadaire (semaine sélectionnée)
  - Sous-onglet planning journalier (programmation, présence, paiement)
  - Sous-onglet calendrier mensuel avec liste des séances
  - Export PDF du planning journalier
- Onglet Caisse:
  - Calcul du montant attendu par jour selon les séances marquées présentes/effectuées
  - Validation du montant réel et suivi des écarts
  - Export PDF du journal de caisse
- Onglet Statistiques:
  - KPI séances, CA facturé, CA encaissé, part CNAM, absences
  - Répartition par kiné et par type d'acte
- Onglet Gestion utilisateurs:
  - Création/modification/désactivation des kinés
  - Modification du nom du cabinet
- Connexion sécurisée:
  - Écran de login au démarrage
  - Mots de passe stockés en hash PBKDF2-SHA256

## Lancer le projet

1. Installer Python 3.10+
2. Installer les dépendances:

```bash
pip install -r requirements.txt
```

3. Exécuter:

```bash
python main.py
```

La base SQLite est créée automatiquement: `mon_kine_data.db`

Compte initial:
- utilisateur: admin
- mot de passe: admin
