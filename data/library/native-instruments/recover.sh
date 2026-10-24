here=$(dirname "$0")
failed=0
sh -e "$here/kontakt-8.sh" || failed=1
sh -e "$here/komplete-kontrol.sh" || failed=1
exit "$failed"
