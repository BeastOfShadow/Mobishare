# Dockerfile personalizzato per Ollama con modello qwen3:latest preinstallato
FROM ollama/ollama:latest


# Avvia temporaneamente il server, scarica i modelli, poi termina il server
RUN ollama serve & \
	sleep 10 && \
	ollama pull nomic-embed-text && \
	ollama pull qwen3:latest && \
	pkill ollama

# Avvia normalmente il server all'avvio del container
ENTRYPOINT ["ollama", "serve"]
