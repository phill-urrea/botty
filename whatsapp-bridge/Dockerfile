# WhatsApp Bridge Dockerfile
# Uses Puppeteer for WhatsApp Web automation

FROM tianon/gosu:1.16 AS gosu
FROM node:20-slim
COPY --from=gosu /usr/local/bin/gosu /usr/local/bin/gosu

# Install Chromium dependencies
RUN apt-get update && apt-get install -y \
    chromium \
    fonts-ipafont-gothic \
    fonts-wqy-zenhei \
    fonts-thai-tlwg \
    fonts-kacst \
    fonts-freefont-ttf \
    libxss1 \
    --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*

# Set Puppeteer to use installed Chromium
ENV PUPPETEER_SKIP_CHROMIUM_DOWNLOAD=true
ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium

# Create app directory
WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies (use npm install when package-lock.json is not present)
RUN npm install

# Copy source code
COPY . .

# Build TypeScript
RUN npm run build

# Remove dev dependencies for smaller image
RUN npm prune --omit=dev

# Create logs directory
RUN mkdir -p logs

# Create non-root user (entrypoint runs as root, then execs as botty)
RUN groupadd -r botty && useradd -r -g botty botty
RUN chown -R botty:botty /app

# Entrypoint ensures volume mount dirs are writable by botty, then run app as botty
COPY entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Expose port
EXPOSE 3001

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:3001/health || exit 1

ENTRYPOINT ["/app/entrypoint.sh"]
CMD ["npm", "start"]
