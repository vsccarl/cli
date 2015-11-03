#!/usr/bin/env bash

set -e

BIN_DIR="$( cd $1 && pwd )"

UNAME=`uname`

if [ -z "$RID" ]; then
    if [ "$UNAME" == "Darwin" ]; then
        RID=osx.10.10-x64
    elif [ "$UNAME" == "Linux" ]; then
        RID=ubuntu.14.04-x64
    else
        echo "Unknown OS: $UNAME" 1>&2
        exit 1
    fi
fi

# Replace with a robust method for finding the right crossgen.exe
CROSSGEN_UTIL=~/.dnx/packages/runtime.$RID.Microsoft.NETCore.Runtime.CoreCLR/1.0.1-beta-23502/tools/crossgen

pushd $BIN_DIR

# Crossgen currently requires itself to be next to mscorlib
cp $CROSSGEN_UTIL $BIN_DIR
chmod +x crossgen

./crossgen -nologo -platform_assemblies_paths $BIN_DIR mscorlib.dll 

./crossgen -nologo -platform_assemblies_paths $BIN_DIR System.Collections.Immutable.dll 

./crossgen -nologo -platform_assemblies_paths $BIN_DIR System.Reflection.Metadata.dll 

./crossgen -nologo -platform_assemblies_paths $BIN_DIR Microsoft.CodeAnalysis.dll 

./crossgen -nologo -platform_assemblies_paths $BIN_DIR Microsoft.CodeAnalysis.CSharp.dll 

./crossgen -nologo -platform_assemblies_paths $BIN_DIR Microsoft.CodeAnalysis.VisualBasic.dll 

./crossgen -nologo -platform_assemblies_paths $BIN_DIR csc.exe 

./crossgen -nologo -platform_assemblies_paths $BIN_DIR vbc.exe 
