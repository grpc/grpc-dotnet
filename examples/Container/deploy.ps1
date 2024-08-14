docker compose -f .\docker-compose.yml build container-frontend
docker compose -f .\docker-compose.yml build container-backend

kubectl delete -f .\Kubernetes\deploy-backend.yml
kubectl apply -f .\Kubernetes\deploy-backend.yml

kubectl delete -f .\Kubernetes\deploy-frontend.yml
kubectl apply -f .\Kubernetes\deploy-frontend.yml
