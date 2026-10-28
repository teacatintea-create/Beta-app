#!/usr/bin/env bash
# Проверка компиляции без Unity и без лицензии: скрипты игры собираются
# компилятором .NET против справочных сборок UnityEngine и UnityEditor с NuGet.
# Ловит опечатки и вызовы несуществующего API. Работу игры не проверяет.
# Нужны dotnet SDK 8, curl и unzip.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="${1:-$(mktemp -d)}"
mkdir -p "$WORK/refs" "$WORK/rt" "$WORK/ed"

fetch() { # id version dir
  local nupkg="$WORK/refs/$1.$2.nupkg"
  [ -f "$nupkg" ] || curl -fsSL -o "$nupkg" "https://api.nuget.org/v3-flatcontainer/$1/$2/$1.$2.nupkg"
  rm -rf "$3" && unzip -q -o "$nupkg" -d "$3"
}
# Ближайшие к 2022.3 сборки, что есть на NuGet; API, которым пользуется игра, с тех пор не менялся
fetch unityengine.modules 2021.3.33 "$WORK/refs/engine"
fetch unity3d.sdk 2021.1.14.1 "$WORK/refs/sdk"

shopt -s nullglob
ENGINE=("$WORK"/refs/engine/lib/net35/*.dll)
echo "Справочных сборок UnityEngine: ${#ENGINE[@]}"
if [ "${#ENGINE[@]}" -lt 10 ] || [ ! -f "$WORK/refs/sdk/lib/UnityEditor.dll" ]; then
  echo "Справочные сборки Unity не найдены:" >&2
  find "$WORK/refs" -maxdepth 3 >&2
  exit 1
fi

refs() {
  for f in "${ENGINE[@]}"; do
    echo "    <Reference Include=\"$(basename "$f" .dll)\"><HintPath>$f</HintPath></Reference>"
  done
}

project() { # dir name sources extra-refs
  cat > "$1/$2.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <AssemblyName>$2</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup><Compile Include="$3/**/*.cs" /></ItemGroup>
  <ItemGroup>
$(refs)
$4
  </ItemGroup>
</Project>
EOF
}

# SDK выбирается по global.json в текущей папке: закрепляем 8.x, на нём проверка отлажена
cd "$WORK"
echo '{"sdk":{"version":"8.0.100","rollForward":"latestFeature"}}' > global.json
dotnet --list-sdks
echo "Выбран .NET SDK $(dotnet --version)"

project "$WORK/rt" Assembly-CSharp "$ROOT/Assets/Kosynka/Scripts" ""
dotnet build "$WORK/rt/Assembly-CSharp.csproj" -nologo -c Release -o "$WORK/rt/out"

project "$WORK/ed" Assembly-CSharp-Editor "$ROOT/Assets/Kosynka/Editor" \
  "    <Reference Include=\"UnityEditor\"><HintPath>$WORK/refs/sdk/lib/UnityEditor.dll</HintPath></Reference>
    <Reference Include=\"Assembly-CSharp\"><HintPath>$WORK/rt/out/Assembly-CSharp.dll</HintPath></Reference>"
dotnet build "$WORK/ed/Assembly-CSharp-Editor.csproj" -nologo -c Release -o "$WORK/ed/out"

echo "Скрипты Kosynka компилируются."
