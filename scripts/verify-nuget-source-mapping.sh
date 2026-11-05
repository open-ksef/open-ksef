#!/usr/bin/env bash
# Dependency-confusion guard: proves src/nuget.config never takes KSeF.Client* from nuget.org, using local feeds in place of both real ones.
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
cd "$work"

mkdir pack feeds feeds/public feeds/cirfmf feeds/empty
cat > pack/pack.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
XML
for id in KSeF.Client KSeF.Client.Core; do
  dotnet pack pack/pack.csproj -c Release -v q -o feeds/public -p:PackageId="$id" -p:Version=2.9.0
  rm -rf pack/obj pack/bin
done
cp feeds/public/*.nupkg feeds/cirfmf/

mkdir app
cat > app/app.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="KSeF.Client" Version="[2.9.0]" />
    <PackageReference Include="KSeF.Client.Core" Version="[2.9.0]" />
  </ItemGroup>
</Project>
XML

cp "$repo/src/nuget.config" mapped.config
dotnet nuget update source CIRFMF --source https://nuget.pkg.github.com/CIRFMF/index.json \
  --username ci --password dummy --store-password-in-clear-text --configfile mapped.config >/dev/null

config() {
  sed -e "s#https://api.nuget.org/v3/index.json#../feeds/public#" \
      -e "s#https://nuget.pkg.github.com/CIRFMF/index.json#../feeds/$1#" mapped.config
}

restore() {
  rm -rf app/obj app/bin
  NUGET_PACKAGES="$PWD/global-$1" dotnet restore app/app.csproj --configfile app/nuget.config 2>&1
}

fail() { echo "FAIL: $*"; exit 1; }

config empty | sed '/<packageSourceMapping>/,/<\/packageSourceMapping>/d' > app/nuget.config
out=$(restore control) || fail "control: restore without mapping should reach the public feed: $out"
grep -Eq 'feeds.{1,2}public' global-control/ksef.client/2.9.0/.nupkg.metadata || fail "control: package not from public feed"
echo "ok: without mapping, restore takes the package from the public feed"

config empty > app/nuget.config
if out=$(restore public-only); then fail "mapped restore took a public-only package: $out"; fi
for id in KSeF.Client KSeF.Client.Core; do
  grep -q "NU1101.*$id" <<<"$out" || fail "expected NU1101 for $id: $out"
done
[ ! -e global-public-only/ksef.client ] || fail "package landed in the global packages folder"
echo "ok: with mapping, a package only on the public feed fails restore (NU1101)"

config cirfmf > app/nuget.config
out=$(restore both) || fail "mapped restore with both feeds failed: $out"
for id in ksef.client ksef.client.core; do
  grep -Eq 'feeds.{1,2}cirfmf' "global-both/$id/2.9.0/.nupkg.metadata" || fail "$id not from CIRFMF"
done
echo "ok: with mapping, a package on both feeds comes from CIRFMF"
