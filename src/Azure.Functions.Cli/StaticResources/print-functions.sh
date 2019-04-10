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

for d in */; do
    d=$(echo $d | tr -d '/')
    if [ -f "${d}/function.json" ]; then
        echo "\"${d}\": "
        cat "${d}/function.json"
        echo ','
    fi
done
echo '}'
echo '}'
