#!/usr/bin/env bash
#
# Writes the .csproj/.sln that a C# language server needs.
#
# Unity only generates these when an External Script Editor (VS Code, Rider) is
# selected in Preferences, and this project has none installed. The files are
# gitignored build artifacts either way, so generating them here costs nothing
# and keeps the editor preference free.
#
# Re-run after adding a package or a new script folder.
#
# Known noise: 48 diagnostics that Unity itself does not raise -- 44x CS0617 on
# CreateAssetMenu's named arguments, and 4x CS0246 on NavMeshPath. Unity compiles
# against unstripped assemblies from a path this cannot see, so the shipped module
# DLLs disagree about those two types. Navigation, hover and find-references are
# unaffected; treat only those two symbols' errors as false.
set -euo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_DATA="${UNITY_DATA:-$HOME/Unity/Hub/Editor/6000.6.0f1/Editor/Data}"

[ -d "$UNITY_DATA/Managed" ] || { echo "Unity Data not found at $UNITY_DATA (set UNITY_DATA)" >&2; exit 127; }

emit_refs() {
  local editor="${1:-no}"
  # Only the module folder. Managed/UnityEngine.dll is the pre-modular monolith and
  # defines rival copies of the same types -- referencing both makes the compiler
  # bind CreateAssetMenu to the old shape, where menuName/fileName are readonly.
  for dll in "$UNITY_DATA/Managed/UnityEngine/"*.dll; do
    [ -f "$dll" ] || continue
    name="$(basename "$dll" .dll)"
    # UnityEditor*.dll sits in this folder too and carries a rival
    # CreateAssetMenuAttribute. Referenced from the runtime project it makes the
    # attribute ambiguous, and the compiler binds the shape whose named
    # arguments are readonly fields -- CS0617 on every ScriptableObject.
    case "$name" in UnityEngine) continue ;; esac
    printf '    <Reference Include="%s"><HintPath>%s</HintPath><Private>false</Private></Reference>\n' \
           "$name" "$dll"
  done
  # Package assemblies Unity already compiled. Editor-only ones are harmless in
  # both projects: an over-broad reference set costs nothing to a language server.
  for dll in "$PROJECT/Library/ScriptAssemblies/"*.dll; do
    [ -f "$dll" ] || continue
    name="$(basename "$dll" .dll)"
    case "$name" in Assembly-CSharp|Assembly-CSharp-Editor) continue ;; esac
    printf '    <Reference Include="%s"><HintPath>%s</HintPath><Private>false</Private></Reference>\n' "$name" "$dll"
  done
}

write_csproj() {
  local out="$1" srcdir="$2" editor="$3"
  {
    cat <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DisableImplicitNuGetFallbackFolder>true</DisableImplicitNuGetFallbackFolder>
    <AssemblyName>$(basename "$out" .csproj)</AssemblyName>
    <NoWarn>\$(NoWarn);CS0649;CS0169</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$srcdir/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
XML
    emit_refs "$editor"
    if [ "$editor" = "yes" ]; then
      printf '    <Reference Include="UnityEditor"><HintPath>%s/Managed/UnityEditor.dll</HintPath><Private>false</Private></Reference>\n' "$UNITY_DATA"
      printf '    <Reference Include="UnityEditor.CoreModule"><HintPath>%s/Managed/UnityEditor.CoreModule.dll</HintPath><Private>false</Private></Reference>\n' "$UNITY_DATA"
    fi
    echo '  </ItemGroup>'
    [ "$editor" = "yes" ] && echo '  <ItemGroup><ProjectReference Include="Assembly-CSharp.csproj" /></ItemGroup>'
    echo '</Project>'
  } > "$PROJECT/$out"
  echo "wrote $out"
}

write_csproj "Assembly-CSharp.csproj"        "Assets/Scripts/Runtime" no
write_csproj "Assembly-CSharp-Editor.csproj" "Assets/Scripts/Editor"  yes

cat > "$PROJECT/Mini_FPS_Shooting_Game.sln" <<'SLN'
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Assembly-CSharp", "Assembly-CSharp.csproj", "{8B2E0B1A-0001-0000-0000-000000000001}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Assembly-CSharp-Editor", "Assembly-CSharp-Editor.csproj", "{8B2E0B1A-0002-0000-0000-000000000002}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{8B2E0B1A-0001-0000-0000-000000000001}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{8B2E0B1A-0001-0000-0000-000000000001}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{8B2E0B1A-0002-0000-0000-000000000002}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{8B2E0B1A-0002-0000-0000-000000000002}.Debug|Any CPU.Build.0 = Debug|Any CPU
	EndGlobalSection
EndGlobal
SLN
echo "wrote Mini_FPS_Shooting_Game.sln"
