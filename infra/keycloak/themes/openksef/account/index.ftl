<#assign portal = (realm.getClientByClientId('openksef-portal-web').getBaseUrl())!''>
<#assign target = portal?remove_ending('/') + '/settings/security'>
<!DOCTYPE html>
<html lang="pl">
<head>
    <meta charset="utf-8">
    <meta http-equiv="refresh" content="0;url=${target}">
    <title>Bezpieczeństwo – OpenKSeF</title>
    <script><#outputformat "JavaScript">location.replace(${target?c})</#outputformat></script>
</head>
<body>
    <a href="${target}">Przejdź do ustawień OpenKSeF</a>
</body>
</html>
