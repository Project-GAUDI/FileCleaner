#! /bin/sh

cat application.info

exec dotnet FileCleaner.dll "$@"
