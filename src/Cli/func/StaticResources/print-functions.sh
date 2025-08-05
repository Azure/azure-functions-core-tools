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
    first=1
    for d in */; do
        d=$(echo $d | tr -d '/')
        if [ -f "${d}/function.json" ]; then
            [ $first -eq 0 ] && echo ','
            first=0
            echo "\"${d}\": "
            cat "${d}/function.json"
        fi
    done
fi

echo '}'
echo '}'
