#! /bin/bash

cat application.info

exec dotnet FileCleaner.dll "$@"
