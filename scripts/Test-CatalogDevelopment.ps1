[CmdletBinding()]
param([string]$BaseUrl = 'http://localhost:5138')

$ErrorActionPreference = 'Stop'
$uri = [Uri]$BaseUrl
if ($uri.Host -notin @('localhost', '127.0.0.1') -or $uri.Scheme -notin @('http', 'https')) {
    throw 'This smoke check only targets the local development API.'
}
$BaseUrl = $BaseUrl.TrimEnd('/')
# Read-only checks: never create clinical or catalogue records in the development DB.
$health = Invoke-RestMethod "$BaseUrl/api/health"
if ($health.status -ne 'Healthy') { throw 'Health check failed.' }
foreach ($resource in @('medical-analyses', 'analysis-panels')) {
    $page = Invoke-RestMethod "$BaseUrl/api/${resource}?search=STEP03_SYNTHETIC_SMOKE&page=1&pageSize=2"
    if ($page.page -ne 1 -or $page.pageSize -ne 2 -or $null -eq $page.items -or $null -eq $page.totalCount) {
        throw "Invalid paginated response for $resource."
    }
    Write-Output "GET /api/$resource : OK (paged response, read-only)"
}
$openApi = Invoke-RestMethod "$BaseUrl/openapi/v1.json"
foreach ($path in @('/api/medical-analyses', '/api/analysis-panels', '/api/health')) {
    if ($null -eq $openApi.paths.PSObject.Properties[$path]) { throw "Missing OpenAPI path: $path" }
}
Write-Output 'Health and OpenAPI: OK. No development data was inserted, updated or deleted.'
