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
    sed -nzE 's/^\[(.+\n {4}"name": "([^"]+)".+)\]$/"\2": \1/p' functions.metadata
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
