-- ============================================================
-- Doodle Drive — amorçage du compte administrateur
-- À exécuter UNE FOIS sur la base cloud_perso après avoir créé
-- les tables (schéma fourni : users / folders / folder_permissions).
--
-- Ce hash BCrypt correspond au mot de passe : admin
-- >>> CHANGEZ-LE dès la première connexion via le panneau Admin <<<
-- ============================================================

USE cloud_perso;

-- Insère l'admin par défaut s'il n'existe pas déjà.
INSERT INTO users (username, password_hash, role)
SELECT 'admin', '$2b$11$jeHRLgxD8La3W7GSWzd6nOxZkEMiOJV6GAMKUyxhSV32nGFljkkTi', 'admin'
WHERE NOT EXISTS (SELECT 1 FROM users WHERE username = 'admin');

-- Pour repartir de zéro sur le mot de passe admin (= 'admin') :
-- UPDATE users
--   SET password_hash = '$2b$11$jeHRLgxD8La3W7GSWzd6nOxZkEMiOJV6GAMKUyxhSV32nGFljkkTi'
--   WHERE username = 'admin';
