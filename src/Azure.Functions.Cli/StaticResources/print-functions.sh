#! /bin/sh

if [ -z "$AzureWebJobsScriptRoot" ]; then
    cd /home/site/wwwroot
else
    cd "$AzureWebJobsScriptRoot"
fi

echo '{'
echo '"hostJson":'
if [ -f "host.json" ]; then
    cat host.json
else
    echo '{ }'
fi

echo ','

echo '"functionsJson": {'

if [ -f "functions.metadata" ]; then
    cat functions.metadata | \
        perl -0 -pe 's/^\[//; s/\]$//; s/(\n {2}\{\n {4}"name": "([^"]+)".+?\n {2}\}\,?)/  "\2": \1\n/gsm'
else
    for d in */; do
        d=$(echo $d | tr -d '/')
        if [ -f "${d}/function.json" ]; then
            echo "\"${d}\": "
            cat "${d}/function.json"
            echo ','
        fi
    done
fi

echo '}'
echo '}'
