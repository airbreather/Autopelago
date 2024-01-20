#!/bin/sh
for f in *.dot; do dot -Tpng -O $f; done
