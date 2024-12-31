#!/bin/zsh

if [ $# -ne 2 ]; then
    echo "args: <input-exe-to-sign> <output-signed-exe>"
    exit 1
fi

SCRIPT_DIR="$(dirname "$(realpath "$0")")"
osslsigncode sign \
    -verbose \
    -pkcs11engine /usr/lib/engines-3/pkcs11.so \
    -pkcs11module $SCRIPT_DIR/sc30pkcs11-3.0.6.69-MS.so \
    -certs $SCRIPT_DIR/057a7e4e121dbaecc28e2a3e5d982dd2.pem \
    -key $(
        openssl x509 \
            -in $SCRIPT_DIR/057a7e4e121dbaecc28e2a3e5d982dd2.pem \
            -ext subjectKeyIdentifier \
            -noout |
        tail -n1 |
        sed 's/[^0-9a-fA-F]//g'
    ) \
    -askpass \
    -h sha256 \
    -t http://timestamp.digicert.com \
    -in $1 \
    -out $2
